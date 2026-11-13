export interface CreateOrderItemRequest {
  productId: number;
  quantity: number;
}

export interface CreateOrderRequest {
  items: CreateOrderItemRequest[];
}

export interface OrderItemResponse {
  productId: number | null;
  productName: string;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
}

export interface OrderResponse {
  id: number;
  createdAtUtc: string;
  status: string;
  totalAmount: number;
  items: OrderItemResponse[];
}

export interface InsufficientStockItemResponse {
  productId: number;
  productName: string;
  requestedQuantity: number;
  availableQuantity: number;
}

export interface UpdateOrderStatusRequest {
  status: string;
}