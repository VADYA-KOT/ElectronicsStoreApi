
import {
  Component,
  ElementRef,
  OnInit,
  OnDestroy,
  ViewChild,
  computed,
  inject,
  signal
} from '@angular/core';

import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';

import {
  Router,
  RouterLink
} from '@angular/router';

import {
  map,
  of,
  switchMap,
  tap
} from 'rxjs';

import { Product } from '../../../core/models/product.model';

import {
  ProductService,
  ProductWriteRequest
} from '../../../core/services/product.service';

import {
  CategoryService,
  CategorySpecification
} from '../../../core/services/category.service';

import { AuthService } from '../../../core/services/auth.service';

interface AdminCategory {
  id: number;
  name: string;
}

interface ProductFormState {
  name: string;
  price: number | null;
  oldPrice: number | null;
  quantity: number;
  categoryId: number | null;
  description: string;
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
  selector: 'app-admin-products',
  standalone: true,
  imports: [
    RouterLink,
    FormsModule
  ],
  templateUrl: './admin-products.component.html',
  styleUrl: './admin-products.component.css'
})
export class AdminProductsComponent implements OnInit, OnDestroy {
  private readonly productService = inject(ProductService);
  private readonly categoryService = inject(CategoryService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  private readonly pageSize = 12;
  private previewObjectUrl: string | null = null;

  // Номер последнего запроса характеристик.
  // Нужен, чтобы старый HTTP-ответ не перезаписал новые данные.
  private specificationRequestVersion = 0;

  @ViewChild('productFormSection')
  private productFormSection?: ElementRef<HTMLElement>;

  protected readonly products = signal<Product[]>([]);
  protected readonly categories = signal<AdminCategory[]>([]);

  protected readonly isLoading = signal(false);
  protected readonly isLoadingCategories = signal(false);
  protected readonly isSaving = signal(false);

  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);
  protected readonly categoriesErrorMessage = signal<string | null>(null);

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

  // ================================================
  // ФОРМА ТОВАРА
  // ================================================

  protected readonly showForm = signal(false);
  protected readonly editingProduct = signal<Product | null>(null);

  protected readonly selectedImage = signal<File | null>(null);
  protected readonly previewUrl = signal<string | null>(null);

  protected readonly deletingProductId = signal<number | null>(null);
  protected readonly deletingImageProductId = signal<number | null>(null);

  protected searchTerm = '';

  protected productForm: ProductFormState = this.createEmptyForm();

  // ================================================
  // ХАРАКТЕРИСТИКИ ТОВАРА
  // ================================================

  protected readonly categorySpecifications =
    signal<CategorySpecification[]>([]);

  protected readonly specificationValues =
    signal<Record<number, string>>({});

  protected readonly isLoadingSpecifications = signal(false);

  protected readonly specificationsErrorMessage =
    signal<string | null>(null);

  // ================================================
  // ИНИЦИАЛИЗАЦИЯ
  // ================================================

  ngOnInit(): void {
    if (
      !this.authService.isAuthenticated ||
      !this.authService.isAdmin
    ) {
      void this.router.navigate(['/']);
      return;
    }

    this.loadCategories();
    this.loadProducts(1);
  }

  ngOnDestroy(): void {
    this.clearPreviewUrl();

    // Игнорируем ответы незавершённых запросов.
    this.specificationRequestVersion++;
  }

  // ================================================
  // ПРОКРУТКА К ФОРМЕ
  // ================================================

  private scrollToForm(): void {
    requestAnimationFrame(() => {
      this.productFormSection?.nativeElement.scrollIntoView({
        behavior: 'smooth',
        block: 'start'
      });
    });
  }

  // ================================================
  // СОЗДАНИЕ ПУСТОЙ ФОРМЫ
  // ================================================

  private createEmptyForm(): ProductFormState {
    return {
      name: '',
      price: null,
      oldPrice: null,
      quantity: 0,
      categoryId: null,
      description: ''
    };
  }

  // ================================================
  // СОЗДАНИЕ ТОВАРА
  // ================================================

  protected openCreateForm(): void {
    this.resetForm();

    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.showForm.set(true);
    this.scrollToForm();
  }

  // ================================================
  // РЕДАКТИРОВАНИЕ ТОВАРА
  // ================================================

  protected openEditForm(product: Product): void {
    this.resetForm();

    this.editingProduct.set(product);

    this.productForm = {
      name: product.name,
      price: product.price,
      oldPrice: product.oldPrice,
      quantity: product.quantity,
      categoryId: product.categoryId,
      description: product.description ?? ''
    };

    // Восстанавливаем значения уже сохранённых характеристик.
    const initialValues: Record<number, string> = {};

    for (const specification of product.specifications) {
      initialValues[specification.categorySpecificationId] =
        specification.value;
    }

    // Загружаем шаблоны характеристик категории.
    this.loadCategorySpecifications(
      product.categoryId,
      initialValues
    );

    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.showForm.set(true);
    this.scrollToForm();
  }

  protected closeForm(): void {
    if (this.isSaving()) {
      return;
    }

    this.resetForm();
    this.showForm.set(false);
  }

  private resetForm(): void {
    this.clearPreviewUrl();

    this.selectedImage.set(null);
    this.editingProduct.set(null);

    this.productForm = this.createEmptyForm();

    this.resetSpecificationState();
  }

  // ================================================
  // СБРОС ХАРАКТЕРИСТИК
  // ================================================

  private resetSpecificationState(): void {
    this.specificationRequestVersion++;

    this.categorySpecifications.set([]);
    this.specificationValues.set({});

    this.isLoadingSpecifications.set(false);
    this.specificationsErrorMessage.set(null);
  }

  // ================================================
  // ЗАГРУЗКА КАТЕГОРИЙ
  // ================================================

  protected loadCategories(): void {
    this.isLoadingCategories.set(true);
    this.categoriesErrorMessage.set(null);

    this.categoryService.getCategories().subscribe({
      next: categories => {
        this.categories.set(categories);
        this.isLoadingCategories.set(false);
      },

      error: (error: unknown) => {
        console.error('Ошибка загрузки категорий:', error);

        this.categoriesErrorMessage.set(
          'Не удалось загрузить категории.'
        );

        this.isLoadingCategories.set(false);
      }
    });
  }

  // ================================================
  // ВЫБОР КАТЕГОРИИ ТОВАРА
  // ================================================

  protected onCategoryChanged(categoryId: number | null): void {
    this.productForm.categoryId = categoryId;

    // При смене категории старые значения характеристик
    // не должны попасть в новый товар.
    this.loadCategorySpecifications(categoryId);
  }

  // ================================================
  // ЗАГРУЗКА ШАБЛОНОВ ХАРАКТЕРИСТИК
  // ================================================

  private loadCategorySpecifications(
    categoryId: number | null,
    initialValues: Record<number, string> = {}
  ): void {
    const requestVersion = ++this.specificationRequestVersion;

    this.categorySpecifications.set([]);
    this.specificationValues.set(initialValues);

    this.specificationsErrorMessage.set(null);

    if (categoryId === null) {
      this.isLoadingSpecifications.set(false);
      return;
    }

    this.isLoadingSpecifications.set(true);

    this.categoryService
      .getCategorySpecifications(categoryId)
      .subscribe({
        next: specifications => {
          // Если пользователь уже выбрал другую категорию,
          // игнорируем ответ старого запроса.
          if (requestVersion !== this.specificationRequestVersion) {
            return;
          }

          this.categorySpecifications.set(specifications);
          this.isLoadingSpecifications.set(false);
        },

        error: (error: unknown) => {
          if (requestVersion !== this.specificationRequestVersion) {
            return;
          }

          console.error(
            'Ошибка загрузки характеристик категории:',
            error
          );

          this.isLoadingSpecifications.set(false);

          this.specificationsErrorMessage.set(
            'Не удалось загрузить характеристики категории. Повторите загрузку.'
          );
        }
      });
  }

  // Повторная загрузка характеристик после ошибки.
  protected retryLoadCategorySpecifications(): void {
    this.loadCategorySpecifications(
      this.productForm.categoryId,
      this.specificationValues()
    );
  }

  // ================================================
  // ИЗМЕНЕНИЕ ЗНАЧЕНИЯ ХАРАКТЕРИСТИКИ
  // ================================================

  protected onSpecificationValueChanged(
    specificationId: number,
    value: string
  ): void {
    this.specificationValues.update(current => ({
      ...current,
      [specificationId]: value
    }));
  }

  // ================================================
  // ТОВАРЫ
  // ================================================

  protected loadProducts(
    page: number = this.currentPage(),
    clearMessages: boolean = true
  ): void {
    if (clearMessages) {
      this.errorMessage.set(null);
      this.successMessage.set(null);
    }

    this.isLoading.set(true);

    this.productService.getProducts({
      search: this.searchTerm.trim(),
      page,
      pageSize: this.pageSize,
      sortBy: 'id',
      descending: true
    }).subscribe({
      next: response => {
        this.products.set(response.items);

        this.currentPage.set(response.page);
        this.totalPages.set(response.totalPages);
        this.totalCount.set(response.totalCount);

        this.isLoading.set(false);
      },

      error: (error: unknown) => {
        console.error('Ошибка загрузки товаров:', error);

        this.errorMessage.set(
          this.getErrorMessage(error)
        );

        this.isLoading.set(false);
      }
    });
  }

  protected searchProducts(): void {
    this.loadProducts(1);
  }

  protected clearSearch(): void {
    this.searchTerm = '';
    this.loadProducts(1);
  }

  protected goToPage(page: number): void {
    if (
      page < 1 ||
      page > this.totalPages() ||
      page === this.currentPage() ||
      this.isLoading()
    ) {
      return;
    }

    this.loadProducts(page);
  }

  // ================================================
  // СОЗДАНИЕ И ОБНОВЛЕНИЕ ТОВАРА
  // ================================================

  protected saveProduct(): void {
    if (this.isSaving()) {
      return;
    }

    // Нельзя сохранять товар, пока загружаются шаблоны.
    if (this.isLoadingSpecifications()) {
      this.errorMessage.set(
        'Дождитесь загрузки характеристик выбранной категории.'
      );
      return;
    }

    // Нельзя случайно потерять характеристики,
    // если сервер не смог загрузить шаблоны категории.
    if (this.specificationsErrorMessage()) {
      this.errorMessage.set(
        'Сначала загрузите характеристики категории повторно.'
      );
      return;
    }

    const name = this.productForm.name.trim();
    const price = Number(this.productForm.price);
    const oldPrice = this.productForm.oldPrice;
    const quantity = Number(this.productForm.quantity);

    if (!name || name.length > 200) {
      this.errorMessage.set(
        'Название товара обязательно и не должно превышать 200 символов.'
      );
      return;
    }

    if (
      !Number.isFinite(price) ||
      price <= 0 ||
      price > 99999999.99
    ) {
      this.errorMessage.set(
        'Укажите корректную цену товара.'
      );
      return;
    }

    if (
      oldPrice !== null &&
      (
        !Number.isFinite(oldPrice) ||
        oldPrice <= price ||
        oldPrice > 99999999.99
      )
    ) {
      this.errorMessage.set(
        'Старая цена должна быть выше текущей.'
      );
      return;
    }

    if (
      !Number.isInteger(quantity) ||
      quantity < 0
    ) {
      this.errorMessage.set(
        'Количество товара должно быть целым неотрицательным числом.'
      );
      return;
    }

    // Собираем значения характеристик из динамической формы.
    // Пустые поля не отправляем, поскольку backend принимает
    // только непустые значения.
    const specifications = this.categorySpecifications()
      .map(specification => ({
        categorySpecificationId: specification.id,
        value: (
          this.specificationValues()[specification.id] ?? ''
        ).trim()
      }))
      .filter(specification => specification.value.length > 0);

    if (
      specifications.some(specification =>
        specification.value.length > 500
      )
    ) {
      this.errorMessage.set(
        'Значение характеристики не должно превышать 500 символов.'
      );
      return;
    }

    const request: ProductWriteRequest = {
      name,
      price,
      oldPrice,
      quantity,
      categoryId: this.productForm.categoryId,
      description:
        this.productForm.description.trim() || null,
      specifications
    };

    const editing = this.editingProduct();

    this.errorMessage.set(null);
    this.successMessage.set(null);
    this.isSaving.set(true);

    const wasEditing = editing !== null;

    const saveRequest$ = editing
      ? this.productService
          .updateProduct(editing.id, request)
          .pipe(
            map(() => ({
              id: editing.id,
              product: editing
            }))
          )
      : this.productService
          .createProduct(request)
          .pipe(
            map(product => ({
              id: product.id,
              product
            }))
          );

    let productDataSaved = false;

    saveRequest$
      .pipe(
        tap(result => {
          productDataSaved = true;

          if (!this.editingProduct()) {
            this.editingProduct.set(result.product);
          }
        }),

        switchMap(result => {
          const file = this.selectedImage();

          if (!file) {
            return of(result.id);
          }

          return this.productService
            .uploadProductImage(result.id, file)
            .pipe(
              map(() => result.id)
            );
        })
      )
      .subscribe({
        next: () => {
          this.isSaving.set(false);

          this.closeForm();

          this.loadProducts(1);

          this.successMessage.set(
            wasEditing
              ? 'Товар успешно обновлён.'
              : 'Товар успешно добавлен.'
          );
        },

        error: (error: unknown) => {
          this.isSaving.set(false);

          if (productDataSaved) {
            this.errorMessage.set(
              'Данные товара сохранены, но фотографию загрузить не удалось. Проверьте файл и повторите сохранение.'
            );

            this.loadProducts(this.currentPage(), false);
            return;
          }

          this.errorMessage.set(
            this.getErrorMessage(error)
          );
        }
      });
  }

  // ================================================
  // УДАЛЕНИЕ ТОВАРА
  // ================================================

  protected deleteProduct(product: Product): void {
    if (this.deletingProductId() !== null) {
      return;
    }

    const confirmed = window.confirm(
      `Удалить товар «${product.name}»? Это действие нельзя отменить.`
    );

    if (!confirmed) {
      return;
    }

    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.deletingProductId.set(product.id);

    this.productService.deleteProduct(product.id).subscribe({
      next: () => {
        this.deletingProductId.set(null);

        const nextPage =
          this.products().length === 1 &&
          this.currentPage() > 1
            ? this.currentPage() - 1
            : this.currentPage();

        this.loadProducts(nextPage);

        this.successMessage.set(
          `Товар «${product.name}» удалён.`
        );
      },

      error: (error: unknown) => {
        this.deletingProductId.set(null);

        this.errorMessage.set(
          this.getErrorMessage(error)
        );
      }
    });
  }

  // ================================================
  // ФОТОГРАФИИ ТОВАРОВ
  // ================================================

  protected onImageSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];

    if (!file) {
      return;
    }

    const allowedTypes = [
      'image/jpeg',
      'image/png',
      'image/webp'
    ];

    if (!allowedTypes.includes(file.type)) {
      this.errorMessage.set(
        'Выберите фотографию в формате JPEG, PNG или WebP.'
      );

      input.value = '';
      return;
    }

    if (file.size > 5 * 1024 * 1024) {
      this.errorMessage.set(
        'Размер фотографии не должен превышать 5 МБ.'
      );

      input.value = '';
      return;
    }

    this.errorMessage.set(null);

    this.clearPreviewUrl();

    this.selectedImage.set(file);

    this.previewObjectUrl = URL.createObjectURL(file);
    this.previewUrl.set(this.previewObjectUrl);
  }

  protected clearSelectedImage(
    input?: HTMLInputElement
  ): void {
    this.selectedImage.set(null);
    this.clearPreviewUrl();

    if (input) {
      input.value = '';
    }
  }

  protected formImageUrl(): string | null {
    const preview = this.previewUrl();

    if (preview) {
      return preview;
    }

    const editing = this.editingProduct();

    return editing
      ? this.productService.getImageUrl(editing.imageUrl)
      : null;
  }

  protected getProductImage(product: Product): string | null {
    return this.productService.getImageUrl(
      product.imageUrl
    );
  }

  protected deleteProductImage(product: Product): void {
    if (
      !product.imageUrl ||
      this.deletingImageProductId() !== null
    ) {
      return;
    }

    const confirmed = window.confirm(
      `Удалить фотографию товара «${product.name}»?`
    );

    if (!confirmed) {
      return;
    }

    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.deletingImageProductId.set(product.id);

    this.productService
      .deleteProductImage(product.id)
      .subscribe({
        next: () => {
          this.deletingImageProductId.set(null);

          this.loadProducts(this.currentPage());

          this.successMessage.set(
            `Фотография товара «${product.name}» удалена.`
          );
        },

        error: (error: unknown) => {
          this.deletingImageProductId.set(null);

          this.errorMessage.set(
            this.getErrorMessage(error)
          );
        }
      });
  }

  private clearPreviewUrl(): void {
    if (this.previewObjectUrl) {
      URL.revokeObjectURL(this.previewObjectUrl);
      this.previewObjectUrl = null;
    }

    this.previewUrl.set(null);
  }

  // ================================================
  // ФОРМАТИРОВАНИЕ
  // ================================================

  protected formatPrice(price: number): string {
    return new Intl.NumberFormat('ru-RU', {
      style: 'currency',
      currency: 'BYN'
    }).format(price);
  }

  protected formatQuantity(quantity: number): string {
    if (quantity === 0) {
      return 'Нет в наличии';
    }

    if (quantity <= 5) {
      return `Осталось: ${quantity} шт.`;
    }

    return `В наличии: ${quantity} шт.`;
  }

  // ================================================
  // ОБРАБОТКА ОШИБОК
  // ================================================

  private getErrorMessage(error: unknown): string {
    if (!(error instanceof HttpErrorResponse)) {
      return 'Произошла непредвиденная ошибка.';
    }

    if (error.status === 0) {
      return 'Не удалось связаться с сервером.';
    }

    if (error.status === 401) {
      void this.router.navigate(['/auth'], {
        queryParams: {
          returnUrl: '/admin/products'
        }
      });

      return 'Необходимо войти в систему повторно.';
    }

    if (error.status === 403) {
      return 'Недостаточно прав для выполнения операции.';
    }

    if (error.status === 404) {
      return 'Товар не найден. Обновите список.';
    }

    if (error.status === 409) {
      return this.getServerMessage(error.error) ??
        'Операция конфликтует с текущими данными.';
    }

    if (error.status === 413) {
      return 'Размер фотографии превышает допустимые 5 МБ.';
    }

    if (error.status === 400) {
      return this.getServerMessage(error.error) ??
        'Сервер отклонил запрос. Проверьте введённые данные.';
    }

    if (error.status >= 500) {
      return 'На сервере произошла ошибка.';
    }

    return 'Не удалось выполнить операцию.';
  }

  private getServerMessage(body: unknown): string | null {
    if (typeof body !== 'object' || body === null) {
      return null;
    }

    const response = body as Record<string, unknown>;

    const message =
      response['message'] ??
      response['detail'] ??
      response['title'];

    return typeof message === 'string'
      ? message
      : null;
  }
}
