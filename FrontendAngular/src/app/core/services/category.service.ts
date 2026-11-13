
import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

import { Category } from '../models/category.model';
import { API_BASE_URL } from '../config/api.config';

export interface CategoryImageUploadResponse {
  message: string;
  imageUrl: string;
}

export interface CategorySpecification {
  id: number;
  categoryId: number;
  code: string;
  name: string;
  unit: string | null;
  sortOrder: number;
}

export interface SaveCategorySpecificationRequest {
  code: string;
  name: string;
  unit: string | null;
  sortOrder: number;
}

@Injectable({
  providedIn: 'root'
})
export class CategoryService {
  private readonly http = inject(HttpClient);

  // ================================================
  // КАТЕГОРИИ
  // ================================================

  // Получение всех категорий.
  getCategories(): Observable<Category[]> {
    return this.http.get<Category[]>(
      `${API_BASE_URL}/api/categories`
    );
  }

  // Получение категории по ID.
  getCategoryById(id: number): Observable<Category> {
    return this.http.get<Category>(
      `${API_BASE_URL}/api/categories/${id}`
    );
  }

  // Создание категории.
  createCategory(name: string): Observable<Category> {
    return this.http.post<Category>(
      `${API_BASE_URL}/api/categories`,
      { name }
    );
  }

  // Изменение названия категории.
  updateCategory(
    id: number,
    name: string
  ): Observable<Category> {
    return this.http.put<Category>(
      `${API_BASE_URL}/api/categories/${id}`,
      { name }
    );
  }

  // Удаление категории.
  deleteCategory(id: number): Observable<void> {
    return this.http.delete<void>(
      `${API_BASE_URL}/api/categories/${id}`
    );
  }

  // ================================================
  // ИЗОБРАЖЕНИЯ КАТЕГОРИЙ
  // ================================================

  // Загрузка или замена фотографии категории.
  uploadCategoryImage(
    categoryId: number,
    file: File
  ): Observable<CategoryImageUploadResponse> {
    const formData = new FormData();

    formData.append('file', file);

    return this.http.post<CategoryImageUploadResponse>(
      `${API_BASE_URL}/api/categories/${categoryId}/image`,
      formData
    );
  }

  // Удаление фотографии категории.
  deleteCategoryImage(categoryId: number): Observable<void> {
    return this.http.delete<void>(
      `${API_BASE_URL}/api/categories/${categoryId}/image`
    );
  }

  // ================================================
  // ХАРАКТЕРИСТИКИ КАТЕГОРИЙ
  // ================================================

  // Получение шаблонов характеристик категории.
  getCategorySpecifications(
    categoryId: number
  ): Observable<CategorySpecification[]> {
    return this.http.get<CategorySpecification[]>(
      `${API_BASE_URL}/api/categories/${categoryId}/specifications`
    );
  }

  // Создание характеристики категории.
  createCategorySpecification(
    categoryId: number,
    request: SaveCategorySpecificationRequest
  ): Observable<CategorySpecification> {
    return this.http.post<CategorySpecification>(
      `${API_BASE_URL}/api/categories/${categoryId}/specifications`,
      request
    );
  }

  // Изменение характеристики категории.
  updateCategorySpecification(
    categoryId: number,
    specificationId: number,
    request: SaveCategorySpecificationRequest
  ): Observable<CategorySpecification> {
    return this.http.put<CategorySpecification>(
      `${API_BASE_URL}/api/categories/${categoryId}/specifications/${specificationId}`,
      request
    );
  }

  // Удаление характеристики категории.
  deleteCategorySpecification(
    categoryId: number,
    specificationId: number
  ): Observable<void> {
    return this.http.delete<void>(
      `${API_BASE_URL}/api/categories/${categoryId}/specifications/${specificationId}`
    );
  }
}
