import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

export interface RawMaterialItem {
  materialId: string;
  materialCode: string;
  name: string;
  category: string;
  unitOfMeasure: string;
  quantityOnHand: number;
  reorderLevel: number;
  unitCost: number;
  valuationLkr: number;
  storageLocation: string;
  notes?: string;
  isActive: boolean;
  isLowStock: boolean;
  isCriticalStock: boolean;
  stockStatus: 'HEALTHY' | 'LOW STOCK' | 'CRITICAL';
  updatedAt: string;
}

export interface RawStockDashboardData {
  totalSkus: number;
  totalValuationLkr: number;
  lowStockCount: number;
  criticalStockCount: number;
  activeSuppliersCount: number;
  materials: RawMaterialItem[];
}

export interface CreateRawMaterialPayload {
  materialCode: string;
  name: string;
  category: string;
  unitOfMeasure: string;
  quantityOnHand: number;
  reorderLevel: number;
  unitCost: number;
  storageLocation: string;
  notes?: string;
  preferredSupplierId?: string;
  leadTimeDays?: number;
}

export interface GoodsReceiptPayload {
  materialId: string;
  supplierId?: string;
  quantityReceived: number;
  unitCost: number;
  moisturePct: number;
  receivedBy: string;
  notes?: string;
}

export interface GoodsReceiptRecord {
  receiptId: string;
  grnNumber: string;
  materialId: string;
  materialCode: string;
  materialName: string;
  supplierId?: string;
  supplierName?: string;
  quantityReceived: number;
  unitOfMeasure: string;
  unitCost: number;
  moisturePct: number;
  receivedBy: string;
  notes?: string;
  receivedAt: string;
}

export interface ConsumptionPayload {
  jobId: string;
  materialId: string;
  quantityIssued: number;
  artisanName: string;
  notes?: string;
}

export interface ConsumptionRecord {
  jobMaterialId: string;
  jobId: string;
  materialId: string;
  materialCode: string;
  materialName: string;
  unitOfMeasure: string;
  quantityIssued: number;
  artisanName: string;
  notes?: string;
  issuedAt: string;
  triggerImpact: string;
}

export interface LowStockQueueItem {
  materialId: string;
  materialCode: string;
  name: string;
  category: string;
  unitOfMeasure: string;
  quantityOnHand: number;
  reorderLevel: number;
  deficitQuantity: number;
  suggestedOrderQty: number;
  unitCost: number;
  preferredSupplierName?: string;
  preferredSupplierPhone?: string;
  leadTimeDays?: number;
  severity: 'CRITICAL' | 'LOW';
}

export interface SupplierItem {
  supplierId: string;
  supplierCode: string;
  companyName: string;
  contactPerson: string;
  phone: string;
  email: string;
  address: string;
  primaryCategory: string;
  isActive: boolean;
  rating: number;
  materialsSuppliedCount: number;
  avgLeadTimeDays: number;
  contractedMaterials: {
    materialId: string;
    materialCode: string;
    materialName: string;
    unitPrice: number;
    leadTimeDays: number;
    isPreferred: boolean;
  }[];
}

@Injectable({
  providedIn: 'root'
})
export class RawMaterialsService {
  private readonly http = inject(HttpClient);

  readonly dashboard = signal<RawStockDashboardData | null>(null);
  readonly loading = signal<boolean>(false);
  readonly error = signal<string | null>(null);

  loadDashboard(category?: string, search?: string): Observable<RawStockDashboardData> {
    this.loading.set(true);
    this.error.set(null);

    let params = new HttpParams();
    if (category && category !== 'All') params = params.set('category', category);
    if (search && search.trim()) params = params.set('search', search.trim());

    return this.http.get<RawStockDashboardData>('/api/raw-materials', { params }).pipe(
      tap({
        next: (data) => {
          this.dashboard.set(data);
          this.loading.set(false);
        },
        error: (err) => {
          this.error.set(err?.error?.message || 'Failed to load raw materials inventory.');
          this.loading.set(false);
        }
      })
    );
  }

  getMaterialById(id: string): Observable<RawMaterialItem> {
    return this.http.get<RawMaterialItem>(`/api/raw-materials/${id}`);
  }

  createMaterial(payload: CreateRawMaterialPayload): Observable<RawMaterialItem> {
    return this.http.post<RawMaterialItem>('/api/raw-materials', payload);
  }

  updateMaterial(id: string, payload: Partial<CreateRawMaterialPayload> & { isActive?: boolean }): Observable<RawMaterialItem> {
    return this.http.put<RawMaterialItem>(`/api/raw-materials/${id}`, payload);
  }

  postGoodsReceipt(payload: GoodsReceiptPayload): Observable<GoodsReceiptRecord> {
    return this.http.post<GoodsReceiptRecord>('/api/raw-materials/grn', payload);
  }

  listGoodsReceipts(limit = 50): Observable<GoodsReceiptRecord[]> {
    return this.http.get<GoodsReceiptRecord[]>('/api/raw-materials/grn', { params: { limit: limit.toString() } });
  }

  issueMaterial(payload: ConsumptionPayload): Observable<ConsumptionRecord> {
    return this.http.post<ConsumptionRecord>('/api/raw-materials/consumption', payload);
  }

  listConsumptions(limit = 50): Observable<ConsumptionRecord[]> {
    return this.http.get<ConsumptionRecord[]>('/api/raw-materials/consumption', { params: { limit: limit.toString() } });
  }

  getLowStockQueue(): Observable<LowStockQueueItem[]> {
    return this.http.get<LowStockQueueItem[]>('/api/raw-materials/low-stock');
  }

  listSuppliers(category?: string, search?: string): Observable<SupplierItem[]> {
    let params = new HttpParams();
    if (category && category !== 'All') params = params.set('category', category);
    if (search && search.trim()) params = params.set('search', search.trim());
    return this.http.get<SupplierItem[]>('/api/suppliers', { params });
  }

  createSupplier(payload: {
    supplierCode: string;
    companyName: string;
    contactPerson: string;
    phone: string;
    email: string;
    address: string;
    primaryCategory: string;
    initialMaterialId?: string;
    initialUnitPrice?: number;
    initialLeadTimeDays?: number;
  }): Observable<SupplierItem> {
    return this.http.post<SupplierItem>('/api/suppliers', payload);
  }

  updateSupplier(id: string, payload: {
    companyName: string;
    contactPerson: string;
    phone: string;
    email: string;
    address: string;
    primaryCategory: string;
    isActive: boolean;
  }): Observable<SupplierItem> {
    return this.http.put<SupplierItem>(`/api/suppliers/${id}`, payload);
  }
}
