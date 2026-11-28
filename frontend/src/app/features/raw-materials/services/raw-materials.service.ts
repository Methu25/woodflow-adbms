import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, switchMap, tap } from 'rxjs';
import { API_URL } from '../../../core/api-url.token';
import { AuthService } from '../../../core/auth/auth.service';

export interface RawMaterialItem {
  materialId: number;
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
  preferredSupplierId?: number;
  leadTimeDays?: number;
}

export interface GoodsReceiptPayload {
  materialId: number;
  supplierId?: number;
  quantityReceived: number;
  unitCost: number;
  moisturePct: number;
  receivedBy: string;
  notes?: string;
}

export interface GoodsReceiptRecord {
  receiptId: number;
  grnNumber: string;
  materialId: number;
  materialCode: string;
  materialName: string;
  supplierId?: number;
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
  materialId: number;
  quantityIssued: number;
  artisanName: string;
  notes?: string;
}

export interface ConsumptionRecord {
  jobMaterialId: number;
  jobId: string;
  materialId: number;
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
  materialId: number;
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
  supplierId: number;
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
    materialId: number;
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
  private readonly auth = inject(AuthService);
  private readonly apiBase = inject(API_URL);
  private readonly rawMaterialsUrl = `${this.apiBase}/raw-materials`;
  private readonly suppliersUrl = `${this.apiBase}/suppliers`;

  readonly dashboard = signal<RawStockDashboardData | null>(null);
  readonly loading = signal<boolean>(false);
  readonly error = signal<string | null>(null);

  loadDashboard(category?: string, search?: string): Observable<RawStockDashboardData> {
    this.loading.set(true);
    this.error.set(null);

    let params = new HttpParams();
    if (category && category !== 'All') params = params.set('category', category);
    if (search && search.trim()) params = params.set('search', search.trim());

    return this.http.get<RawStockDashboardData>(this.rawMaterialsUrl, { params }).pipe(
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

  getMaterialById(id: number): Observable<RawMaterialItem> {
    return this.http.get<RawMaterialItem>(`${this.rawMaterialsUrl}/${id}`);
  }

  listCatalog(includeInactive = false): Observable<RawMaterialItem[]> {
    let params = new HttpParams();
    if (includeInactive) {
      params = params.set('includeInactive', 'true');
    }
    return this.http.get<RawMaterialItem[]>(`${this.rawMaterialsUrl}/catalog`, { params });
  }

  createMaterial(payload: CreateRawMaterialPayload): Observable<RawMaterialItem> {
    return this.auth.refreshCsrf().pipe(
      switchMap(() => this.http.post<RawMaterialItem>(this.rawMaterialsUrl, payload))
    );
  }

  updateMaterial(id: number, payload: Partial<CreateRawMaterialPayload> & { isActive?: boolean }): Observable<RawMaterialItem> {
    return this.auth.refreshCsrf().pipe(
      switchMap(() => this.http.put<RawMaterialItem>(`${this.rawMaterialsUrl}/${id}`, payload))
    );
  }

  postGoodsReceipt(payload: GoodsReceiptPayload): Observable<GoodsReceiptRecord> {
    return this.auth.refreshCsrf().pipe(
      switchMap(() => this.http.post<GoodsReceiptRecord>(`${this.rawMaterialsUrl}/grn`, payload))
    );
  }

  listGoodsReceipts(limit = 50): Observable<GoodsReceiptRecord[]> {
    return this.http.get<GoodsReceiptRecord[]>(`${this.rawMaterialsUrl}/grn`, { params: { limit: limit.toString() } });
  }

  issueMaterial(payload: ConsumptionPayload): Observable<ConsumptionRecord> {
    return this.auth.refreshCsrf().pipe(
      switchMap(() => this.http.post<ConsumptionRecord>(`${this.rawMaterialsUrl}/consumption`, payload))
    );
  }

  listConsumptions(limit = 50): Observable<ConsumptionRecord[]> {
    return this.http.get<ConsumptionRecord[]>(`${this.rawMaterialsUrl}/consumption`, { params: { limit: limit.toString() } });
  }

  getLowStockQueue(): Observable<LowStockQueueItem[]> {
    return this.http.get<LowStockQueueItem[]>(`${this.rawMaterialsUrl}/low-stock`);
  }

  listSuppliers(category?: string, search?: string): Observable<SupplierItem[]> {
    let params = new HttpParams();
    if (category && category !== 'All') params = params.set('category', category);
    if (search && search.trim()) params = params.set('search', search.trim());
    return this.http.get<SupplierItem[]>(this.suppliersUrl, { params });
  }

  createSupplier(payload: {
    supplierCode: string;
    companyName: string;
    contactPerson: string;
    phone: string;
    email: string;
    address: string;
    primaryCategory: string;
    initialMaterialId?: number;
    initialUnitPrice?: number;
    initialLeadTimeDays?: number;
  }): Observable<SupplierItem> {
    return this.auth.refreshCsrf().pipe(
      switchMap(() => this.http.post<SupplierItem>(this.suppliersUrl, payload))
    );
  }

  updateSupplier(id: number, payload: {
    companyName: string;
    contactPerson: string;
    phone: string;
    email: string;
    address: string;
    primaryCategory: string;
    isActive: boolean;
  }): Observable<SupplierItem> {
    return this.auth.refreshCsrf().pipe(
      switchMap(() => this.http.put<SupplierItem>(`${this.suppliersUrl}/${id}`, payload))
    );
  }
}
