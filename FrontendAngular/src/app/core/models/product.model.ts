
export interface ProductSpecification {
  categorySpecificationId: number;
  code: string;
  name: string;
  unit: string | null;
  value: string;
  sortOrder: number;
}

export interface Product {
  id: number;
  name: string;
  price: number;
  oldPrice: number | null;
  quantity: number;
  imageUrl: string | null;
  categoryId: number | null;
  categoryName: string | null;
  description: string | null;
  specifications: ProductSpecification[];
}

export interface ProductListResponse {
  items: Product[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}
