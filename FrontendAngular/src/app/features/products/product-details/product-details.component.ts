
import {
  Component,
  DestroyRef,
  OnInit,
  inject,
  signal
} from '@angular/core';

import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import {
  EMPTY,
  catchError,
  finalize,
  switchMap,
  tap
} from 'rxjs';

import { Product } from '../../../core/models/product.model';
import { ProductService } from '../../../core/services/product.service';
import { CartService } from '../../../core/services/cart.service';

@Component({
  selector: 'app-product-details',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './product-details.component.html',
  styleUrl: './product-details.component.css'
})
export class ProductDetailsComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly productService = inject(ProductService);
  private readonly cartService = inject(CartService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly product = signal<Product | null>(null);

  protected readonly isLoading = signal(true);
  protected readonly errorMessage = signal<string | null>(null);
  protected readonly cartMessage = signal<string | null>(null);

  ngOnInit(): void {
    this.route.paramMap
      .pipe(
        switchMap(params => {
          const id = Number(params.get('id'));

          this.product.set(null);
          this.errorMessage.set(null);
          this.cartMessage.set(null);
          this.isLoading.set(true);

          if (!Number.isSafeInteger(id) || id <= 0) {
            this.errorMessage.set(
              'Указан некорректный идентификатор товара.'
            );

            this.isLoading.set(false);

            return EMPTY;
          }

          return this.productService.getProductById(id).pipe(
            tap(product => {
              this.product.set(product);
            }),

            catchError((error: unknown) => {
              console.error('Ошибка загрузки товара:', error);

              if (
                error instanceof HttpErrorResponse &&
                error.status === 404
              ) {
                this.errorMessage.set(
                  'Товар не найден. Возможно, он был удалён.'
                );
              } else {
                this.errorMessage.set(
                  'Не удалось загрузить товар. Проверьте соединение с сервером.'
                );
              }

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
  }

  // Добавляем текущий товар в корзину.
  protected addToCart(): void {
    const currentProduct = this.product();

    if (!currentProduct) {
      return;
    }

    const added = this.cartService.addItem(currentProduct);

    this.cartMessage.set(
      added
        ? 'Товар добавлен в корзину.'
        : 'Достигнуто максимальное доступное количество этого товара.'
    );
  }

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
