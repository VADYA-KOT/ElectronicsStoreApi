
import {
  Component,
  DestroyRef,
  ElementRef,
  OnDestroy,
  OnInit,
  ViewChild,
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
  takeUntilDestroyed
} from '@angular/core/rxjs-interop';

import {
  map,
  of,
  switchMap,
  tap
} from 'rxjs';

import { Category } from '../../../core/models/category.model';

import {
  CategoryService,
  CategorySpecification,
  SaveCategorySpecificationRequest
} from '../../../core/services/category.service';

import { ProductService } from '../../../core/services/product.service';
import { AuthService } from '../../../core/services/auth.service';

interface CategorySpecificationFormState {
  code: string;
  name: string;
  unit: string;
  sortOrder: number;
}

@Component({
  selector: 'app-admin-categories',
  standalone: true,
  imports: [
    RouterLink,
    FormsModule
  ],
  templateUrl: './admin-categories.component.html',
  styleUrl: './admin-categories.component.css'
})
export class AdminCategoriesComponent implements OnInit, OnDestroy {
  private readonly categoryService = inject(CategoryService);
  private readonly productService = inject(ProductService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  @ViewChild('categoryFormSection')
  private categoryFormSection?: ElementRef<HTMLElement>;

  protected readonly categories = signal<Category[]>([]);

  protected readonly isLoading = signal(false);
  protected readonly isSaving = signal(false);

  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);

  protected readonly showForm = signal(false);
  protected readonly editingCategory = signal<Category | null>(null);

  protected readonly deletingCategoryId = signal<number | null>(null);
  protected readonly deletingImageCategoryId = signal<number | null>(null);

  protected readonly categoryName = signal('');

  protected readonly selectedImage = signal<File | null>(null);
  protected readonly previewUrl = signal<string | null>(null);

  private previewObjectUrl: string | null = null;

  // ================================================
  // ХАРАКТЕРИСТИКИ КАТЕГОРИЙ
  // ================================================

  protected readonly categorySpecifications =
    signal<Record<number, CategorySpecification[]>>({});

  protected readonly expandedSpecificationCategoryId =
    signal<number | null>(null);

  protected readonly loadingSpecificationsCategoryId =
    signal<number | null>(null);

  protected readonly showSpecificationForm = signal(false);

  protected readonly specificationCategoryId =
    signal<number | null>(null);

  protected readonly editingSpecification =
    signal<CategorySpecification | null>(null);

  protected readonly isSavingSpecification = signal(false);

  protected readonly deletingSpecificationId =
    signal<number | null>(null);

  protected specificationForm: CategorySpecificationFormState =
    this.createEmptySpecificationForm();

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
  }

  ngOnDestroy(): void {
    this.clearPreviewUrl();
  }

  // ================================================
  // ПРОКРУТКА К ФОРМЕ
  // ================================================

  private scrollToForm(): void {
    requestAnimationFrame(() => {
      this.categoryFormSection?.nativeElement.scrollIntoView({
        behavior: 'smooth',
        block: 'start'
      });
    });
  }

  // ================================================
  // ЗАГРУЗКА КАТЕГОРИЙ
  // ================================================

  protected loadCategories(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.categoryService.getCategories()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: categories => {
          this.categories.set(categories);
          this.isLoading.set(false);
        },

        error: (error: unknown) => {
          console.error('Ошибка загрузки категорий:', error);

          this.errorMessage.set(
            this.getErrorMessage(error)
          );

          this.isLoading.set(false);
        }
      });
  }

  // ================================================
  // ФОРМА КАТЕГОРИИ
  // ================================================

  protected openCreateForm(): void {
    this.resetForm();

    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.showForm.set(true);
    this.scrollToForm();
  }

  protected openEditForm(category: Category): void {
    this.resetForm();

    this.editingCategory.set(category);
    this.categoryName.set(category.name);

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
    this.editingCategory.set(null);
    this.categoryName.set('');
  }

  // ================================================
  // СОЗДАНИЕ И РЕДАКТИРОВАНИЕ КАТЕГОРИИ
  // ================================================

  protected saveCategory(): void {
    if (this.isSaving()) {
      return;
    }

    const name = this.categoryName().trim();

    if (name.length < 2 || name.length > 100) {
      this.errorMessage.set(
        'Название категории должно содержать от 2 до 100 символов.'
      );
      return;
    }

    const editing = this.editingCategory();

    this.errorMessage.set(null);
    this.successMessage.set(null);
    this.isSaving.set(true);

    const saveRequest$ = editing
      ? this.categoryService.updateCategory(editing.id, name)
      : this.categoryService.createCategory(name);

    let savedCategory: Category | null = null;

    saveRequest$
      .pipe(
        tap(category => {
          savedCategory = category;
          this.editingCategory.set(category);
        }),

        switchMap(category => {
          const file = this.selectedImage();

          if (!file) {
            return of(category);
          }

          return this.categoryService
            .uploadCategoryImage(category.id, file)
            .pipe(
              map(result => ({
                ...category,
                imageUrl: result.imageUrl
              }))
            );
        }),

        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: category => {
          this.isSaving.set(false);

          this.closeForm();
          this.loadCategories();

          this.successMessage.set(
            editing
              ? `Категория «${category.name}» обновлена.`
              : `Категория «${category.name}» создана.`
          );
        },

        error: (error: unknown) => {
          this.isSaving.set(false);

          if (savedCategory !== null) {
            this.editingCategory.set(savedCategory);

            this.errorMessage.set(
              'Категория сохранена, но фотографию загрузить не удалось. Проверьте файл и повторите попытку.'
            );

            this.loadCategoriesWithoutClearingForm();
            return;
          }

          this.errorMessage.set(
            this.getErrorMessage(error)
          );
        }
      });
  }

  private loadCategoriesWithoutClearingForm(): void {
    this.categoryService.getCategories()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: categories => {
          this.categories.set(categories);
        },

        error: (error: unknown) => {
          console.error(
            'Ошибка обновления списка категорий:',
            error
          );
        }
      });
  }

  // ================================================
  // УДАЛЕНИЕ КАТЕГОРИИ
  // ================================================

  protected deleteCategory(category: Category): void {
    if (
      this.deletingCategoryId() !== null ||
      this.deletingImageCategoryId() !== null
    ) {
      return;
    }

    if (category.productCount > 0) {
      this.errorMessage.set(
        `Нельзя удалить категорию «${category.name}»: к ней привязано товаров — ${category.productCount}. Сначала переместите или удалите эти товары.`
      );
      return;
    }

    const confirmed = window.confirm(
      `Удалить категорию «${category.name}»? Это действие нельзя отменить.`
    );

    if (!confirmed) {
      return;
    }

    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.deletingCategoryId.set(category.id);

    this.categoryService.deleteCategory(category.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.deletingCategoryId.set(null);

          this.loadCategories();

          this.successMessage.set(
            `Категория «${category.name}» удалена.`
          );
        },

        error: (error: unknown) => {
          this.deletingCategoryId.set(null);

          this.errorMessage.set(
            this.getErrorMessage(error)
          );

          this.loadCategoriesWithoutClearingForm();
        }
      });
  }

  // ================================================
  // ВЫБОР ФОТОГРАФИИ КАТЕГОРИИ
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
        'Выберите изображение в формате JPEG, PNG или WebP.'
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

  protected getFormImageUrl(): string | null {
    const preview = this.previewUrl();

    if (preview) {
      return preview;
    }

    const category = this.editingCategory();

    return category
      ? this.getCategoryImage(category)
      : null;
  }

  // ================================================
  // ФОТОГРАФИИ КАТЕГОРИЙ
  // ================================================

  protected getCategoryImage(category: Category): string | null {
    return this.productService.getImageUrl(
      category.imageUrl
    );
  }

  protected deleteCategoryImage(category: Category): void {
    if (
      !category.imageUrl ||
      this.deletingImageCategoryId() !== null ||
      this.deletingCategoryId() !== null
    ) {
      return;
    }

    const confirmed = window.confirm(
      `Удалить фотографию категории «${category.name}»?`
    );

    if (!confirmed) {
      return;
    }

    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.deletingImageCategoryId.set(category.id);

    this.categoryService
      .deleteCategoryImage(category.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.deletingImageCategoryId.set(null);

          this.loadCategories();

          this.successMessage.set(
            `Фотография категории «${category.name}» удалена.`
          );
        },

        error: (error: unknown) => {
          this.deletingImageCategoryId.set(null);

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
  // ЗАГРУЗКА ХАРАКТЕРИСТИК КАТЕГОРИИ
  // ================================================

  protected getCategorySpecifications(
    categoryId: number
  ): CategorySpecification[] {
    return this.categorySpecifications()[categoryId] ?? [];
  }

  protected toggleCategorySpecifications(category: Category): void {
    if (
      this.expandedSpecificationCategoryId() === category.id
    ) {
      this.expandedSpecificationCategoryId.set(null);
      this.closeSpecificationForm();
      return;
    }

    this.expandedSpecificationCategoryId.set(category.id);
    this.closeSpecificationForm();

    this.loadCategorySpecifications(category.id);
  }

  protected loadCategorySpecifications(categoryId: number): void {
    this.loadingSpecificationsCategoryId.set(categoryId);

    this.categoryService
      .getCategorySpecifications(categoryId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: specifications => {
          this.categorySpecifications.update(current => ({
            ...current,
            [categoryId]: specifications
          }));

          if (
            this.loadingSpecificationsCategoryId() === categoryId
          ) {
            this.loadingSpecificationsCategoryId.set(null);
          }
        },

        error: (error: unknown) => {
          console.error(
            'Ошибка загрузки характеристик категории:',
            error
          );

          if (
            this.loadingSpecificationsCategoryId() === categoryId
          ) {
            this.loadingSpecificationsCategoryId.set(null);
          }

          this.errorMessage.set(
            this.getErrorMessage(error)
          );
        }
      });
  }

  // ================================================
  // ФОРМА ХАРАКТЕРИСТИКИ
  // ================================================

  private createEmptySpecificationForm(
    sortOrder: number = 0
  ): CategorySpecificationFormState {
    return {
      code: '',
      name: '',
      unit: '',
      sortOrder
    };
  }

  protected openCreateSpecificationForm(
    category: Category
  ): void {
    const specifications =
      this.getCategorySpecifications(category.id);

    const nextSortOrder = specifications.length > 0
      ? Math.max(...specifications.map(s => s.sortOrder)) + 10
      : 0;

    this.editingSpecification.set(null);
    this.specificationCategoryId.set(category.id);

    this.specificationForm =
      this.createEmptySpecificationForm(nextSortOrder);

    this.showSpecificationForm.set(true);

    this.errorMessage.set(null);
    this.successMessage.set(null);
  }

  protected openEditSpecificationForm(
    category: Category,
    specification: CategorySpecification
  ): void {
    this.editingSpecification.set(specification);
    this.specificationCategoryId.set(category.id);

    this.specificationForm = {
      code: specification.code,
      name: specification.name,
      unit: specification.unit ?? '',
      sortOrder: specification.sortOrder
    };

    this.showSpecificationForm.set(true);

    this.errorMessage.set(null);
    this.successMessage.set(null);
  }

  protected closeSpecificationForm(): void {
    if (this.isSavingSpecification()) {
      return;
    }

    this.showSpecificationForm.set(false);
    this.editingSpecification.set(null);
    this.specificationCategoryId.set(null);

    this.specificationForm =
      this.createEmptySpecificationForm();
  }

  // ================================================
  // СОХРАНЕНИЕ ХАРАКТЕРИСТИКИ
  // ================================================

  protected saveCategorySpecification(): void {
    if (this.isSavingSpecification()) {
      return;
    }

    const categoryId = this.specificationCategoryId();

    if (categoryId === null) {
      this.errorMessage.set(
        'Не удалось определить категорию характеристики.'
      );
      return;
    }

    const code = this.specificationForm.code
      .trim()
      .toLowerCase();

    const name = this.specificationForm.name.trim();

    const unit = this.specificationForm.unit.trim();

    const sortOrder = Number(
      this.specificationForm.sortOrder
    );

    if (
      code.length < 2 ||
      code.length > 50 ||
      !/^[a-zA-Z0-9_-]+$/.test(code)
    ) {
      this.errorMessage.set(
        'Код должен содержать от 2 до 50 символов: только латинские буквы, цифры, дефис и нижнее подчёркивание.'
      );
      return;
    }

    if (name.length < 2 || name.length > 100) {
      this.errorMessage.set(
        'Название характеристики должно содержать от 2 до 100 символов.'
      );
      return;
    }

    if (unit.length > 30) {
      this.errorMessage.set(
        'Единица измерения не должна превышать 30 символов.'
      );
      return;
    }

    if (
      !Number.isInteger(sortOrder) ||
      sortOrder < 0
    ) {
      this.errorMessage.set(
        'Порядок отображения должен быть целым неотрицательным числом.'
      );
      return;
    }

    const request: SaveCategorySpecificationRequest = {
      code,
      name,
      unit: unit || null,
      sortOrder
    };

    const editing = this.editingSpecification();

    this.errorMessage.set(null);
    this.successMessage.set(null);
    this.isSavingSpecification.set(true);

    const saveRequest$ = editing
      ? this.categoryService.updateCategorySpecification(
          categoryId,
          editing.id,
          request
        )
      : this.categoryService.createCategorySpecification(
          categoryId,
          request
        );

    saveRequest$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: specification => {
          this.isSavingSpecification.set(false);

          this.closeSpecificationForm();

          this.loadCategorySpecifications(categoryId);

          this.successMessage.set(
            editing
              ? `Характеристика «${specification.name}» обновлена.`
              : `Характеристика «${specification.name}» добавлена.`
          );
        },

        error: (error: unknown) => {
          this.isSavingSpecification.set(false);

          this.errorMessage.set(
            this.getErrorMessage(error)
          );
        }
      });
  }

  // ================================================
  // УДАЛЕНИЕ ХАРАКТЕРИСТИКИ
  // ================================================

  protected deleteCategorySpecification(
    category: Category,
    specification: CategorySpecification
  ): void {
    if (
      this.deletingSpecificationId() !== null ||
      this.isSavingSpecification()
    ) {
      return;
    }

    const confirmed = window.confirm(
      `Удалить характеристику «${specification.name}»?`
    );

    if (!confirmed) {
      return;
    }

    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.deletingSpecificationId.set(specification.id);

    this.categoryService
      .deleteCategorySpecification(
        category.id,
        specification.id
      )
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.deletingSpecificationId.set(null);

          this.loadCategorySpecifications(category.id);

          this.successMessage.set(
            `Характеристика «${specification.name}» удалена.`
          );
        },

        error: (error: unknown) => {
          this.deletingSpecificationId.set(null);

          this.errorMessage.set(
            this.getErrorMessage(error)
          );
        }
      });
  }

  // ================================================
  // ФОРМАТИРОВАНИЕ
  // ================================================

  protected getProductCountText(count: number): string {
    const remainder10 = count % 10;
    const remainder100 = count % 100;

    if (remainder100 >= 11 && remainder100 <= 14) {
      return `${count} товаров`;
    }

    if (remainder10 === 1) {
      return `${count} товар`;
    }

    if (remainder10 >= 2 && remainder10 <= 4) {
      return `${count} товара`;
    }

    return `${count} товаров`;
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
          returnUrl: '/admin/categories'
        }
      });

      return 'Необходимо войти в систему повторно.';
    }

    if (error.status === 403) {
      return 'Недостаточно прав для выполнения операции.';
    }

    if (error.status === 404) {
      return 'Категория или характеристика не найдена. Обновите список.';
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
