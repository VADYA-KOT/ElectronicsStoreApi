
import {
  Component,
  DestroyRef,
  OnInit,
  computed,
  inject,
  signal
} from '@angular/core';

import {
  NavigationEnd,
  Router,
  RouterLink,
  RouterOutlet
} from '@angular/router';

import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import {
  EMPTY,
  Subject,
  catchError,
  filter,
  finalize,
  of,
  switchMap,
  tap,
  timer
} from 'rxjs';

import { Category } from './core/models/category.model';
import { Product } from './core/models/product.model';

import { CategoryService } from './core/services/category.service';
import { ProductService } from './core/services/product.service';
import { CartService } from './core/services/cart.service';
import { AuthService } from './core/services/auth.service';

type SortOption =
  | 'newest'
  | 'name-asc'
  | 'price-asc'
  | 'price-desc'
  | 'quantity-desc';

interface ProductLoadRequest {
  page: number;
  debounce: boolean;
}

interface ComparisonSpecificationRow {
  key: string;
  name: string;
  values: string[];
}

type PaginationItem =
  | {
      type: 'page';
      value: number;
    }
  | {
      type: 'ellipsis';
      key: string;
    };

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit {
  private readonly productService = inject(ProductService);
  private readonly categoryService = inject(CategoryService);
  private readonly cartService = inject(CartService);
  private readonly authService = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);

  private readonly productRequests$ = new Subject<ProductLoadRequest>();

  // ========================================
  // PAGE
  // ========================================

  protected readonly isHomePage = signal(
    this.isHomeUrl(this.router.url)
  );

  // ========================================
  // STORE DATA
  // ========================================

  protected readonly products = signal<Product[]>([]);
  protected readonly categories = signal<Category[]>([]);

  // ========================================
  // LOADING AND ERRORS
  // ========================================

  protected readonly isLoading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly isCategoriesLoading = signal(false);
  protected readonly categoriesErrorMessage = signal<string | null>(null);

  // ========================================
  // SEARCH AND FILTERS
  // ========================================

  protected readonly searchQuery = signal('');
  protected readonly selectedCategory = signal<number | null>(null);

  protected readonly categoriesExpanded = signal(false);

  protected readonly minPriceInput = signal('');
  protected readonly maxPriceInput = signal('');

  private readonly appliedMinPrice = signal<number | null>(null);
  private readonly appliedMaxPrice = signal<number | null>(null);

  protected readonly sortOption = signal<SortOption>('newest');
  protected readonly inStockOnly = signal(false);

  protected readonly filterErrorMessage = signal<string | null>(null);

  protected readonly hasActiveFilters = computed(() =>
    this.searchQuery().trim() !== '' ||
    this.selectedCategory() !== null ||
    this.minPriceInput().trim() !== '' ||
    this.maxPriceInput().trim() !== '' ||
    this.appliedMinPrice() !== null ||
    this.appliedMaxPrice() !== null ||
    this.sortOption() !== 'newest' ||
    this.inStockOnly()
  );

  private readonly pageSize = 12;

  // ========================================
  // CATEGORY VIEW
  // ========================================

  protected readonly visibleCategories = computed(() => {
    const categories = this.categories();

    return this.categoriesExpanded()
      ? categories
      : categories.slice(0, 4);
  });

  protected readonly selectedCategoryName = computed(() => {
    const categoryId = this.selectedCategory();

    return this.categories().find(
      category => category.id === categoryId
    )?.name ?? null;
  });

  // ========================================
  // CART AND AUTHENTICATION
  // ========================================

  protected readonly cartCount = computed(
    () => this.cartService.totalQuantity()
  );

  protected readonly isAuthenticated = computed(
    () => this.authService.isAuthenticated
  );

  protected readonly isAdmin = computed(
    () => this.authService.isAdmin
  );

  // ========================================
  // PAGINATION
  // ========================================

  protected readonly currentPage = signal(1);
  protected readonly totalPages = signal(1);
  protected readonly totalCount = signal(0);

  protected readonly paginationItems = computed<PaginationItem[]>(() => {
    const total = this.totalPages();
    const current = this.currentPage();

    if (total <= 7) {
      return Array.from(
        { length: total },
        (_, index) => ({
          type: 'page' as const,
          value: index + 1
        })
      );
    }

    const pageNumbers = new Set<number>([
      1,
      total,
      current - 1,
      current,
      current + 1
    ]);

    if (current <= 4) {
      pageNumbers.add(2);
      pageNumbers.add(3);
      pageNumbers.add(4);
      pageNumbers.add(5);
    }

    if (current >= total - 3) {
      pageNumbers.add(total - 1);
      pageNumbers.add(total - 2);
      pageNumbers.add(total - 3);
      pageNumbers.add(total - 4);
    }

    const sortedPages = [...pageNumbers]
      .filter(page => page >= 1 && page <= total)
      .sort((a, b) => a - b);

    const result: PaginationItem[] = [];

    let previousPage = 0;

    for (const page of sortedPages) {
      if (previousPage > 0 && page - previousPage > 1) {
        result.push({
          type: 'ellipsis',
          key: `ellipsis-${previousPage}-${page}`
        });
      }

      result.push({
        type: 'page',
        value: page
      });

      previousPage = page;
    }

    return result;
  });

  // ========================================
  // PRODUCT COMPARISON
  // ========================================

  protected readonly comparedProducts = signal<Product[]>([]);

  protected readonly comparisonOpen = signal(false);

  protected readonly comparisonMessage = signal<string | null>(null);

  protected readonly comparisonCount = computed(
    () => this.comparedProducts().length
  );

  protected readonly comparisonSpecificationRows =
    computed<ComparisonSpecificationRow[]>(() => {
      const products = this.comparedProducts();

      const rows = new Map<
        string,
        {
          name: string;
          values: Map<number, string>;
        }
      >();

      products.forEach((product, productIndex) => {
        for (const specification of product.specifications ?? []) {
          const key = (
            specification.code ||
            String(specification.categorySpecificationId)
          ).toLowerCase();

          let row = rows.get(key);

          if (!row) {
            row = {
              name: specification.name,
              values: new Map<number, string>()
            };

            rows.set(key, row);
          }

          const value = specification.unit
            ? `${specification.value} ${specification.unit}`
            : specification.value;

          row.values.set(productIndex, value);
        }
      });

      return [...rows.entries()].map(([key, row]) => ({
        key,
        name: row.name,
        values: products.map(
          (_, index) => row.values.get(index) ?? '—'
        )
      }));
    });

  // ========================================
  // VISIBLE PRODUCTS
  // ========================================

  protected readonly visibleProducts = computed(() => {
    return this.products();
  });

  // ========================================
  // INITIALIZATION
  // ========================================

  ngOnInit(): void {
    this.router.events
      .pipe(
        filter(
          (event): event is NavigationEnd =>
            event instanceof NavigationEnd
        ),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe(event => {
        this.isHomePage.set(
          this.isHomeUrl(event.urlAfterRedirects)
        );
      });

    this.productRequests$
      .pipe(
        switchMap(({ page, debounce }) => {
          this.isLoading.set(true);
          this.errorMessage.set(null);

          const delay$ = debounce
            ? timer(350)
            : of(0);

          return delay$.pipe(
            switchMap(() =>
              this.productService.getProducts({
                search: this.searchQuery(),
                categoryId: this.selectedCategory(),
                minPrice: this.appliedMinPrice(),
                maxPrice: this.appliedMaxPrice(),
                page,
                pageSize: this.pageSize,
                sortBy: this.getSortBy(),
                descending: this.isDescending(),
                inStockOnly: this.inStockOnly()
              })
            ),

            tap(response => {
              this.products.set(response.items);

              this.currentPage.set(response.page);
              this.totalPages.set(response.totalPages);
              this.totalCount.set(response.totalCount);
            }),

            catchError((error: unknown) => {
              console.error('Ошибка загрузки товаров:', error);

              this.errorMessage.set(
                'Не удалось загрузить товары. Проверьте соединение с сервером.'
              );

              return EMPTY;
            }),

            finalize(() => {
              this.isLoading.set(false);
            })
          );
        }),

        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe();

    this.loadCategories();
    this.loadProducts(1);
  }

  private isHomeUrl(url: string): boolean {
    const path = url.split(/[?#]/)[0] ?? '';

    return path === '' || path === '/';
  }

  // ========================================
  // CATEGORIES
  // ========================================

  protected toggleCategories(): void {
    this.categoriesExpanded.update(
      expanded => !expanded
    );
  }

  protected loadCategories(): void {
    this.isCategoriesLoading.set(true);
    this.categoriesErrorMessage.set(null);

    this.categoryService.getCategories()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: categories => {
          this.categories.set(categories);
          this.isCategoriesLoading.set(false);
        },

        error: (error: unknown) => {
          console.error('Ошибка загрузки категорий:', error);

          this.categoriesErrorMessage.set(
            'Не удалось загрузить категории. Проверьте соединение с сервером.'
          );

          this.isCategoriesLoading.set(false);
        }
      });
  }

  protected selectCategory(categoryId: number): void {
    this.selectedCategory.set(categoryId);
    this.loadProducts(1);

    document.getElementById('products')?.scrollIntoView({
      behavior: 'smooth',
      block: 'start'
    });
  }

  // Получение изображения непосредственно из категории.
  protected getCategoryImage(categoryId: number): string | null {
    const category = this.categories().find(
      item => item.id === categoryId
    );

    return category
      ? this.getImageUrl(category.imageUrl)
      : null;
  }

  protected getCategoryIcon(categoryName: string): string {
    const name = categoryName.toLowerCase();

    if (
      name.includes('смартфон') ||
      name.includes('телефон') ||
      name.includes('мобильн')
    ) {
      return '📱';
    }

    if (
      name.includes('ноутбук') ||
      name.includes('компьютер') ||
      name.includes('пк')
    ) {
      return '💻';
    }

    if (
      name.includes('планшет')
    ) {
      return '▣';
    }

    if (
      name.includes('наушник') ||
      name.includes('аудио') ||
      name.includes('звук')
    ) {
      return '♫';
    }

    if (
      name.includes('телевизор') ||
      name.includes('монитор')
    ) {
      return '▣';
    }

    if (
      name.includes('аксессуар')
    ) {
      return '⌁';
    }

    if (
      name.includes('бытов')
    ) {
      return '⌂';
    }

    return '◇';
  }

  // ========================================
  // PRODUCTS
  // ========================================

  protected loadProducts(
    page: number,
    debounce: boolean = false
  ): void {
    this.productRequests$.next({
      page,
      debounce
    });
  }

  private getSortBy(): 'id' | 'name' | 'price' | 'quantity' {
    switch (this.sortOption()) {
      case 'name-asc':
        return 'name';

      case 'price-asc':
      case 'price-desc':
        return 'price';

      case 'quantity-desc':
        return 'quantity';

      default:
        return 'id';
    }
  }

  private isDescending(): boolean {
    const option = this.sortOption();

    return option === 'newest' ||
      option === 'price-desc' ||
      option === 'quantity-desc';
  }

  // ========================================
  // SEARCH
  // ========================================

  protected onSearch(event: Event): void {
    const input = event.target as HTMLInputElement;

    this.searchQuery.set(input.value);
    this.loadProducts(1, true);
  }

  // ========================================
  // PRICE FILTER
  // ========================================

  protected onMinPriceChange(event: Event): void {
    this.minPriceInput.set(
      (event.target as HTMLInputElement).value
    );
  }

  protected onMaxPriceChange(event: Event): void {
    this.maxPriceInput.set(
      (event.target as HTMLInputElement).value
    );
  }

  protected applyPriceFilter(): void {
    this.filterErrorMessage.set(null);

    const minText = this.minPriceInput().trim();
    const maxText = this.maxPriceInput().trim();

    const minPrice = minText === ''
      ? null
      : Number(minText);

    const maxPrice = maxText === ''
      ? null
      : Number(maxText);

    if (
      minPrice !== null &&
      (!Number.isFinite(minPrice) || minPrice < 0)
    ) {
      this.filterErrorMessage.set(
        'Минимальная цена должна быть неотрицательным числом.'
      );
      return;
    }

    if (
      maxPrice !== null &&
      (!Number.isFinite(maxPrice) || maxPrice < 0)
    ) {
      this.filterErrorMessage.set(
        'Максимальная цена должна быть неотрицательным числом.'
      );
      return;
    }

    if (
      minPrice !== null &&
      maxPrice !== null &&
      minPrice > maxPrice
    ) {
      this.filterErrorMessage.set(
        'Минимальная цена не может быть выше максимальной.'
      );
      return;
    }

    this.appliedMinPrice.set(minPrice);
    this.appliedMaxPrice.set(maxPrice);

    this.loadProducts(1);
  }

  // ========================================
  // SORTING AND STOCK
  // ========================================

  protected onSortChange(event: Event): void {
    const value = (
      event.target as HTMLSelectElement
    ).value as SortOption;

    const allowedOptions: SortOption[] = [
      'newest',
      'name-asc',
      'price-asc',
      'price-desc',
      'quantity-desc'
    ];

    if (!allowedOptions.includes(value)) {
      return;
    }

    this.sortOption.set(value);
    this.loadProducts(1);
  }

  protected onStockOnlyChange(event: Event): void {
    const checkbox = event.target as HTMLInputElement;

    this.inStockOnly.set(checkbox.checked);
    this.loadProducts(1);
  }

  // ========================================
  // CLEAR FILTERS
  // ========================================

  protected clearFilters(): void {
    this.searchQuery.set('');
    this.selectedCategory.set(null);

    this.minPriceInput.set('');
    this.maxPriceInput.set('');

    this.appliedMinPrice.set(null);
    this.appliedMaxPrice.set(null);

    this.sortOption.set('newest');
    this.inStockOnly.set(false);

    this.filterErrorMessage.set(null);

    this.loadProducts(1);
  }

  // ========================================
  // PAGINATION
  // ========================================

  protected goToPage(page: number): void {
    if (
      page < 1 ||
      page > this.totalPages() ||
      page === this.currentPage()
    ) {
      return;
    }

    this.loadProducts(page);

    document.getElementById('products')?.scrollIntoView({
      behavior: 'smooth',
      block: 'start'
    });
  }

  // ========================================
  // PRODUCT ACTIONS
  // ========================================

  protected openProduct(id: number): void {
    void this.router.navigate(['/products', id]);
  }

  protected addToCart(product: Product): void {
    this.cartService.addItem(product);
  }

  protected getDiscountPercent(product: Product): number {
    if (
      product.oldPrice === null ||
      product.oldPrice <= product.price ||
      product.oldPrice <= 0
    ) {
      return 0;
    }

    return Math.round(
      ((product.oldPrice - product.price) / product.oldPrice) * 100
    );
  }

  // ========================================
  // COMPARISON
  // ========================================

  protected isCompared(productId: number): boolean {
    return this.comparedProducts().some(
      product => product.id === productId
    );
  }

  protected toggleComparison(product: Product): void {
    const current = this.comparedProducts();

    const existingIndex = current.findIndex(
      item => item.id === product.id
    );

    if (existingIndex >= 0) {
      this.comparedProducts.update(items =>
        items.filter(item => item.id !== product.id)
      );

      this.comparisonMessage.set(null);

      if (this.comparedProducts().length < 2) {
        this.comparisonOpen.set(false);
      }

      return;
    }

    if (current.length >= 4) {
      this.comparisonMessage.set(
        'Можно сравнить не более четырёх товаров одновременно.'
      );
      return;
    }

    if (
      current.length > 0 &&
      current[0].categoryId !== product.categoryId
    ) {
      this.comparisonMessage.set(
        'Для корректного сравнения выберите товары одной категории.'
      );
      return;
    }

    this.comparedProducts.update(items => [
      ...items,
      product
    ]);

    this.comparisonMessage.set(null);
  }

  protected removeComparedProduct(productId: number): void {
    this.comparedProducts.update(items =>
      items.filter(product => product.id !== productId)
    );

    if (this.comparedProducts().length < 2) {
      this.comparisonOpen.set(false);
    }

    this.comparisonMessage.set(null);
  }

  protected clearComparison(): void {
    this.comparedProducts.set([]);
    this.comparisonOpen.set(false);
    this.comparisonMessage.set(null);
  }

  protected openComparison(): void {
    if (this.comparedProducts().length < 2) {
      this.comparisonMessage.set(
        'Добавьте минимум два товара для сравнения.'
      );
      return;
    }

    this.comparisonMessage.set(null);
    this.comparisonOpen.set(true);
  }

  protected closeComparison(): void {
    this.comparisonOpen.set(false);
  }

  // ========================================
  // AUTHENTICATION
  // ========================================

  protected logout(): void {
    this.authService.logout();
    void this.router.navigate(['/']);
  }

  // ========================================
  // FORMATTING
  // ========================================

  protected formatPrice(price: number): string {
    return new Intl.NumberFormat('ru-RU', {
      style: 'currency',
      currency: 'BYN'
    }).format(price);
  }

  protected getImageUrl(imageUrl: string | null): string | null {
    return this.productService.getImageUrl(imageUrl);
  }
}
