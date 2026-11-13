
import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

import {
  Product,
  ProductListResponse
} from '../models/product.model';

import { API_BASE_URL } from '../config/api.config';

export interface ProductQuery {
  search?: string;
  categoryId?: number | null;
  minPrice?: number | null;
  maxPrice?: number | null;
  maxQuantity?: number | null;
  sortBy?: 'id' | 'name' | 'price' | 'quantity';
  descending?: boolean;
  inStockOnly?: boolean;
  page?: number;
  pageSize?: number;
}

export interface ProductSpecificationInput {
  categorySpecificationId: number;
  value: string;
}

export interface ProductWriteRequest {
  name: string;
  price: number;
  oldPrice: number | null;
  quantity: number;
  categoryId: number | null;
  description: string | null;
  specifications: ProductSpecificationInput[];
}

export interface ProductImageUploadResponse {
  message: string;
  imageUrl: string;
}

@Injectable({
  providedIn: 'root'
})
export class ProductService {
  private readonly http = inject(HttpClient);

  // Получение списка товаров с фильтрами и пагинацией.
  getProducts(
    query: ProductQuery = {}
  ): Observable<ProductListResponse> {
    let params = new HttpParams()
      .set('page', String(query.page ?? 1))
      .set('pageSize', String(query.pageSize ?? 12));

    const search = query.search?.trim();

    if (search) {
      params = params.set('search', search);
    }

    if (query.categoryId != null) {
      params = params.set(
        'categoryId',
        String(query.categoryId)
      );
    }

    if (query.minPrice != null) {
      params = params.set(
        'minPrice',
        String(query.minPrice)
      );
    }

    if (query.maxPrice != null) {
      params = params.set(
        'maxPrice',
        String(query.maxPrice)
      );
    }

    if (query.maxQuantity != null) {
      params = params.set(
        'maxQuantity',
        String(query.maxQuantity)
      );
    }

    if (query.sortBy) {
      params = params.set('sortBy', query.sortBy);
    }

    if (query.descending !== undefined) {
      params = params.set(
        'descending',
        String(query.descending)
      );
    }

    if (query.inStockOnly !== undefined) {
      params = params.set(
        'inStockOnly',
        String(query.inStockOnly)
      );
    }

    return this.http.get<ProductListResponse>(
      `${API_BASE_URL}/api/products`,
      { params }
    );
  }

  // Получение одного товара по ID.
  getProductById(id: number): Observable<Product> {
    return this.http.get<Product>(
      `${API_BASE_URL}/api/products/${id}`
    );
  }

  // Создание товара.
  createProduct(
    request: ProductWriteRequest
  ): Observable<Product> {
    return this.http.post<Product>(
      `${API_BASE_URL}/api/products`,
      request
    );
  }

  // Обновление товара.
  updateProduct(
    id: number,
    request: ProductWriteRequest
  ): Observable<void> {
    return this.http.put<void>(
      `${API_BASE_URL}/api/products/${id}`,
      request
    );
  }

  // Удаление товара.
  deleteProduct(id: number): Observable<void> {
    return this.http.delete<void>(
      `${API_BASE_URL}/api/products/${id}`
    );
  }

  // Загрузка или замена фотографии товара.
  uploadProductImage(
    productId: number,
    file: File
  ): Observable<ProductImageUploadResponse> {
    const formData = new FormData();

    formData.append('file', file);

    return this.http.post<ProductImageUploadResponse>(
      `${API_BASE_URL}/api/products/${productId}/image`,
      formData
    );
  }

  // Удаление фотографии товара.
  deleteProductImage(
    productId: number
  ): Observable<void> {
    return this.http.delete<void>(
      `${API_BASE_URL}/api/products/${productId}/image`
    );
  }

  // Формирование полного адреса изображения.
  getImageUrl(imageUrl: string | null): string | null {
    if (!imageUrl) {
      return null;
    }

    if (/^https?:\/\//i.test(imageUrl)) {
      return imageUrl;
    }

    return `${API_BASE_URL}${imageUrl.startsWith('/') ? '' : '/'}${imageUrl}`;
  }
}
