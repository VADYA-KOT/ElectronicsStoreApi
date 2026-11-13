import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

import { API_BASE_URL } from '../config/api.config';

import {
  CreateOrderRequest,
  OrderResponse,
  UpdateOrderStatusRequest
} from '../models/order.model';

@Injectable({
  providedIn: 'root'
})
export class OrderApiService {
  private readonly http = inject(HttpClient);

  private readonly apiUrl = `${API_BASE_URL}/api/orders`;

  // Получение истории заказов текущего пользователя.
  getMyOrders(): Observable<OrderResponse[]> {
    return this.http.get<OrderResponse[]>(this.apiUrl);
  }

  // Получение всех заказов для администратора.
  getAllForAdmin(): Observable<OrderResponse[]> {
    return this.http.get<OrderResponse[]>(
      `${this.apiUrl}/admin`
    );
  }

  // Создание заказа.
  createOrder(request: CreateOrderRequest): Observable<OrderResponse> {
    return this.http.post<OrderResponse>(
      this.apiUrl,
      request
    );
  }

  // Изменение статуса заказа.
  updateOrderStatus(
    orderId: number,
    request: UpdateOrderStatusRequest
  ): Observable<OrderResponse> {
    return this.http.patch<OrderResponse>(
      `${this.apiUrl}/${orderId}/status`,
      request
    );
  }
}