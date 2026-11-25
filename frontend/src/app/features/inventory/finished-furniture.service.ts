import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, map, of, switchMap, tap, timeout } from 'rxjs';
import { API_URL } from '../../core/api-url.token';
import { AuthService } from '../../core/auth/auth.service';

export type StockStatus = 'IN_STOCK' | 'LOW_STOCK' | 'OUT_OF_STOCK';

export interface FinishedFurnitureItem {
  productId: number;
  skuCode: string;
  barcode: string;
  itemName: string;
  category: string;
  categoryCode: string;
  primaryWoodType: string;
  retailPrice: number;
  currentStock: number;
  minReorderLevel: number;
  stockStatus: StockStatus;
  dimensionsFinish: string;
  descriptionNotes: string;
  showroomZone: string;
  imageKey: string;
  isActive: boolean;
  restockRequested: boolean;
  restockRequestedQty: number;
  workshopStatusLabel: string | null;
  createdAt: string | null;
}

export interface CategorySummaryCount {
  categoryName: string;
  count: number;
}

export interface FinishedCatalogResponse {
  items: FinishedFurnitureItem[];
  totalActiveSkus: number;
  categoryCounts: CategorySummaryCount[];
}

export interface UpsertFinishedFurnitureRequest {
  skuCode: string;
  barcode: string;
  itemName: string;
  category: string;
  primaryWoodType: string;
  retailPrice: number;
  minReorderLevel: number;
  currentStock: number;
  dimensionsFinish: string;
  descriptionNotes: string;
  showroomZone?: string;
  imageKey?: string;
  isActive?: boolean;
}

export interface StockMovementDto {
  movementId: number;
  movementCode: string;
  productId: number;
  skuCode: string;
  itemName: string;
  movementType: string;
  movementLabel: string;
  quantityChanged: number;
  stockBefore: number;
  stockAfter: number;
  referenceId: string;
  performedByName: string;
  notes: string | null;
  createdAt: string;
}

export interface StockMovementLedgerResponse {
  movements: StockMovementDto[];
  totalCount: number;
  inboundCount: number;
  salesOutCount: number;
  auditAdjustmentCount: number;
}

export interface AuditAdjustmentItemRequest {
  productId: number;
  physicalCount: number;
  reason: string;
}

export interface ApplyStockAuditRequest {
  sessionCode: string;
  items: AuditAdjustmentItemRequest[];
}

export interface StockAuditSessionResultDto {
  sessionCode: string;
  adjustedItemsCount: number;
  netVariance: number;
  appliedAt: string;
  updatedItems: FinishedFurnitureItem[];
}

const FALLBACK_MOVEMENTS: StockMovementDto[] = [
  {
    movementId: 1,
    movementCode: '#MOV-9048',
    productId: 1001,
    skuCode: 'FF-LIV-001',
    itemName: 'Aurelia 3-Piece Lounge Suite',
    movementType: 'POS_SALE',
    movementLabel: 'POS Sale Out',
    quantityChanged: -2,
    stockBefore: 10,
    stockAfter: 8,
    referenceId: 'INV-2026-0892 · Yohan M.',
    performedByName: 'Yohan M.',
    notes: 'POS Sale (trg_pos_stock_deduct)',
    createdAt: '2026-11-13T10:42:00+05:30',
  },
  {
    movementId: 2,
    movementCode: '#MOV-9047',
    productId: 1002,
    skuCode: 'FF-BED-004',
    itemName: 'Kandy Royal King Teak Bed',
    movementType: 'WORKSHOP_COMPLETED',
    movementLabel: 'Workshop In',
    quantityChanged: 3,
    stockBefore: 2,
    stockAfter: 5,
    referenceId: 'PROD-JOB-145 · Sanith N.',
    performedByName: 'Sanith N.',
    notes: 'Workshop Finishing In',
    createdAt: '2026-11-13T09:15:00+05:30',
  },
  {
    movementId: 3,
    movementCode: '#MOV-9046',
    productId: 1003,
    skuCode: 'FF-DIN-002',
    itemName: 'Riviera 6-Chair Dining Set',
    movementType: 'CUSTOM_DELIVERY',
    movementLabel: 'Custom Delivery',
    quantityChanged: -1,
    stockBefore: 3,
    stockAfter: 2,
    referenceId: 'DEL-2026-108 · Dilshan S.',
    performedByName: 'Dilshan S.',
    notes: 'Custom Delivery Dispatch',
    createdAt: '2026-11-12T16:30:00+05:30',
  },
  {
    movementId: 4,
    movementCode: '#MOV-9045',
    productId: 1006,
    skuCode: 'FF-BED-012',
    itemName: 'Royal Arch 3-Door Wardrobe',
    movementType: 'STOCK_CORRECTION',
    movementLabel: 'Audit Adjustment',
    quantityChanged: -1,
    stockBefore: 2,
    stockAfter: 1,
    referenceId: 'AUD-NOV-04 · P Rejishanth',
    performedByName: 'P Rejishanth',
    notes: 'Transferred display unit to Kandy branch',
    createdAt: '2026-11-12T11:20:00+05:30',
  },
  {
    movementId: 5,
    movementCode: '#MOV-9044',
    productId: 1004,
    skuCode: 'FF-OFF-009',
    itemName: 'Moratuwa Executive Suite',
    movementType: 'WORKSHOP_COMPLETED',
    movementLabel: 'Workshop In',
    quantityChanged: 2,
    stockBefore: 2,
    stockAfter: 4,
    referenceId: 'PROD-JOB-139 · Rakshan P.',
    performedByName: 'Rakshan P.',
    notes: 'Workshop Finishing In',
    createdAt: '2026-11-11T15:05:00+05:30',
  },
  {
    movementId: 6,
    movementCode: '#MOV-9043',
    productId: 1005,
    skuCode: 'FF-LIV-007',
    itemName: 'Ceylon Heritage Sofa Bed',
    movementType: 'POS_SALE',
    movementLabel: 'POS Sale Out',
    quantityChanged: -2,
    stockBefore: 2,
    stockAfter: 0,
    referenceId: 'INV-2026-0874 · Yohan M.',
    performedByName: 'Yohan M.',
    notes: 'POS Sale (trg_pos_stock_deduct)',
    createdAt: '2026-11-10T14:50:00+05:30',
  },
  {
    movementId: 7,
    movementCode: '#MOV-9042',
    productId: 1010,
    skuCode: 'FF-LIV-014',
    itemName: 'Teak Verandah Lounge Chair',
    movementType: 'SHOWROOM_RETURN',
    movementLabel: 'Showroom Return',
    quantityChanged: 1,
    stockBefore: 5,
    stockAfter: 6,
    referenceId: 'RET-2026-019 · P Rejishanth',
    performedByName: 'P Rejishanth',
    notes: 'Customer exchange returned to showroom floor',
    createdAt: '2026-11-09T12:10:00+05:30',
  },
];

const FALLBACK_CATALOG_ITEMS: FinishedFurnitureItem[] = [
  {
    productId: 1001,
    skuCode: 'FF-LIV-001',
    barcode: '8901245001928',
    itemName: 'Aurelia 3-Piece Lounge Set',
    category: 'Living Room',
    categoryCode: 'CAT-01',
    primaryWoodType: 'Solid Teak',
    retailPrice: 145000,
    currentStock: 8,
    minReorderLevel: 4,
    stockStatus: 'IN_STOCK',
    dimensionsFinish: '84" × 32" × 30" (3-Seater) · Matte PU Clear Lacquer + 32D Foam Cushions',
    descriptionNotes: 'Hand-crafted solid Burma & Ceylon teak lounge suite with removable linen upholstery. Includes 10-year structural joinery warranty. Standard delivery time: 2–3 days.',
    showroomZone: 'Ground Floor · Bay A2',
    imageKey: 'lounge',
    isActive: true,
    restockRequested: false,
    restockRequestedQty: 0,
    workshopStatusLabel: null,
    createdAt: '2026-11-01T08:00:00Z',
  },
  {
    productId: 1002,
    skuCode: 'FF-BED-004',
    barcode: '8901245001935',
    itemName: 'Kandy Royal King Teak Bed',
    category: 'Bedroom',
    categoryCode: 'CAT-02',
    primaryWoodType: '72"×78"',
    retailPrice: 168500,
    currentStock: 5,
    minReorderLevel: 3,
    stockStatus: 'IN_STOCK',
    dimensionsFinish: '72" × 78" King Frame · Hand-Carved Headboard · Satin Teak Finish',
    descriptionNotes: 'Royal Kandy heritage king bed crafted from kiln-dried Grade-A Ceylon teak with reinforced mortise-and-tenon joints.',
    showroomZone: 'First Floor · Bay B1',
    imageKey: 'bed',
    isActive: true,
    restockRequested: false,
    restockRequestedQty: 0,
    workshopStatusLabel: null,
    createdAt: '2026-11-01T08:00:00Z',
  },
  {
    productId: 1003,
    skuCode: 'FF-DIN-002',
    barcode: '8901245001942',
    itemName: 'Riviera 6-Chair Dining Set',
    category: 'Dining Room',
    categoryCode: 'CAT-03',
    primaryWoodType: 'Burma Teak',
    retailPrice: 192000,
    currentStock: 2,
    minReorderLevel: 4,
    stockStatus: 'LOW_STOCK',
    dimensionsFinish: '72" × 42" Table + 6 Upholstered Teak Dining Chairs · High-Gloss PU',
    descriptionNotes: 'Six-seater solid Burma teak dining suite with stain-resistant fabric seat cushions and bevelled table edge.',
    showroomZone: 'Ground Floor · Bay C1',
    imageKey: 'dining',
    isActive: true,
    restockRequested: false,
    restockRequestedQty: 3,
    workshopStatusLabel: null,
    createdAt: '2026-11-01T08:00:00Z',
  },
  {
    productId: 1004,
    skuCode: 'FF-OFF-009',
    barcode: '8901245001959',
    itemName: 'Moratuwa Executive Suite',
    category: 'Office',
    categoryCode: 'CAT-04',
    primaryWoodType: 'Polished Mahogany',
    retailPrice: 118000,
    currentStock: 4,
    minReorderLevel: 3,
    stockStatus: 'IN_STOCK',
    dimensionsFinish: '66" × 34" Pedestal Desk + Credenza · Rich Mahogany Lacquer',
    descriptionNotes: 'Executive office desk with brass hardware, lockable file drawers, and integrated cable routing.',
    showroomZone: 'First Floor · Bay D2',
    imageKey: 'office',
    isActive: true,
    restockRequested: false,
    restockRequestedQty: 0,
    workshopStatusLabel: null,
    createdAt: '2026-11-01T08:00:00Z',
  },
  {
    productId: 1005,
    skuCode: 'FF-LIV-007',
    barcode: '8901245001966',
    itemName: 'Ceylon Heritage Sofa Bed',
    category: 'Living Room',
    categoryCode: 'CAT-01',
    primaryWoodType: 'Cushioned',
    retailPrice: 94500,
    currentStock: 0,
    minReorderLevel: 3,
    stockStatus: 'OUT_OF_STOCK',
    dimensionsFinish: '76" × 36" Pull-Out Teak Daybed · 32D High-Density Cushioning',
    descriptionNotes: 'Dual-purpose convertible Ceylon teak sofa bed designed for compact urban living rooms.',
    showroomZone: 'Ground Floor · Bay A4',
    imageKey: 'sofabed',
    isActive: true,
    restockRequested: true,
    restockRequestedQty: 4,
    workshopStatusLabel: 'Urgent Restock',
    createdAt: '2026-11-01T08:00:00Z',
  },
  {
    productId: 1006,
    skuCode: 'FF-BED-012',
    barcode: '8901245001973',
    itemName: 'Royal Arch 3-Door Wardrobe',
    category: 'Bedroom',
    categoryCode: 'CAT-02',
    primaryWoodType: 'Nadun & Teak',
    retailPrice: 210000,
    currentStock: 1,
    minReorderLevel: 3,
    stockStatus: 'LOW_STOCK',
    dimensionsFinish: '78" × 60" × 24" 3-Door Almirah · Full Bevelled Dressing Mirror',
    descriptionNotes: 'Spacious three-door solid teak and Nadun wardrobe featuring brass locks, hanging rails, and internal jewellery safe drawer.',
    showroomZone: 'First Floor · Bay B3',
    imageKey: 'wardrobe',
    isActive: true,
    restockRequested: false,
    restockRequestedQty: 3,
    workshopStatusLabel: null,
    createdAt: '2026-11-01T08:00:00Z',
  },
];

@Injectable({ providedIn: 'root' })
export class FinishedFurnitureService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly baseUrl = `${inject(API_URL)}/finished-goods`;

  private readonly localItems = signal<FinishedFurnitureItem[]>([...FALLBACK_CATALOG_ITEMS]);

  listCatalog(category = 'All', search = ''): Observable<FinishedCatalogResponse> {
    const params: Record<string, string> = {};
    if (category && category !== 'All') params['category'] = category;
    if (search.trim()) params['search'] = search.trim();

    return this.http.get<FinishedCatalogResponse>(this.baseUrl, { params }).pipe(
      timeout(12000),
      tap(res => {
        if ((!category || category === 'All') && !search.trim() && res.items.length > 0) {
          this.localItems.set(res.items);
        }
      }),
      catchError(() => of(this.filterLocalCatalog(category, search))),
    );
  }

  getById(productId: number): Observable<FinishedFurnitureItem> {
    return this.http.get<FinishedFurnitureItem>(`${this.baseUrl}/${productId}`).pipe(
      timeout(12000),
      catchError(() => {
        const match = this.localItems().find(i => i.productId === productId) ?? this.localItems()[0];
        return of(match);
      }),
    );
  }

  createItem(request: UpsertFinishedFurnitureRequest): Observable<FinishedFurnitureItem> {
    return this.auth.refreshCsrf().pipe(
      switchMap(() => this.http.post<FinishedFurnitureItem>(this.baseUrl, request)),
      timeout(12000),
      tap(created => {
        this.localItems.update(list => [created, ...list]);
      }),
    );
  }

  updateItem(productId: number, request: UpsertFinishedFurnitureRequest): Observable<FinishedFurnitureItem> {
    return this.auth.refreshCsrf().pipe(
      switchMap(() => this.http.put<FinishedFurnitureItem>(`${this.baseUrl}/${productId}`, request)),
      timeout(12000),
      tap(updated => {
        this.localItems.update(list =>
          list.map(item => (item.productId === productId ? updated : item)),
        );
      }),
    );
  }

  deleteItem(productId: number): Observable<void> {
    return this.auth.refreshCsrf().pipe(
      switchMap(() => this.http.delete<void>(`${this.baseUrl}/${productId}`)),
      timeout(12000),
    );
  }

  listMovements(productId: number | null = null, filterType = 'ALL'): Observable<StockMovementLedgerResponse> {
    const params: Record<string, string> = {};
    if (productId !== null && productId > 0) params['productId'] = String(productId);
    if (filterType && filterType !== 'ALL') params['filterType'] = filterType;

    return this.http.get<StockMovementLedgerResponse>(`${this.baseUrl}/movements`, { params }).pipe(
      timeout(12000),
      catchError(() => of(this.filterLocalMovements(productId, filterType))),
    );
  }

  applyStockAudit(request: ApplyStockAuditRequest): Observable<StockAuditSessionResultDto> {
    return this.auth.refreshCsrf().pipe(
      switchMap(() => this.http.post<StockAuditSessionResultDto>(`${this.baseUrl}/audit`, request)),
      timeout(12000),
      tap(res => {
        if (res.updatedItems?.length) {
          this.localItems.update(list =>
            list.map(item => res.updatedItems.find(u => u.productId === item.productId) ?? item),
          );
        }
      }),
    );
  }

  private filterLocalMovements(productId: number | null, filterType: string): StockMovementLedgerResponse {
    let base = [...FALLBACK_MOVEMENTS];
    if (productId !== null && productId > 0) {
      base = base.filter(m => m.productId === productId);
    }

    const inboundCount = base.filter(m => m.quantityChanged > 0 && m.movementType !== 'STOCK_CORRECTION').length;
    const salesOutCount = base.filter(m => m.quantityChanged < 0 && m.movementType !== 'STOCK_CORRECTION').length;
    const auditAdjustmentCount = base.filter(m => m.movementType === 'STOCK_CORRECTION').length;

    let filtered = base;
    if (filterType && filterType !== 'ALL') {
      if (filterType === 'INBOUND') {
        filtered = base.filter(m => m.quantityChanged > 0 && m.movementType !== 'STOCK_CORRECTION');
      } else if (filterType === 'SALES_OUT') {
        filtered = base.filter(m => m.quantityChanged < 0 && m.movementType !== 'STOCK_CORRECTION');
      } else if (filterType === 'AUDIT') {
        filtered = base.filter(m => m.movementType === 'STOCK_CORRECTION');
      }
    }

    return {
      movements: filtered,
      totalCount: base.length,
      inboundCount,
      salesOutCount,
      auditAdjustmentCount,
    };
  }

  private filterLocalCatalog(category: string, search: string): FinishedCatalogResponse {
    const all = this.localItems().filter(i => i.isActive);
    const countsMap = new Map<string, number>();
    for (const item of all) {
      countsMap.set(item.category, (countsMap.get(item.category) ?? 0) + 1);
    }
    const categoryCounts: CategorySummaryCount[] = Array.from(countsMap.entries()).map(
      ([categoryName, count]) => ({ categoryName, count }),
    );

    let filtered = all;
    if (category && category !== 'All') {
      filtered = filtered.filter(i => i.category.toLowerCase() === category.toLowerCase());
    }
    if (search.trim()) {
      const term = search.trim().toLowerCase();
      filtered = filtered.filter(
        i =>
          i.itemName.toLowerCase().includes(term) ||
          i.skuCode.toLowerCase().includes(term) ||
          i.barcode.toLowerCase().includes(term) ||
          i.category.toLowerCase().includes(term) ||
          i.primaryWoodType.toLowerCase().includes(term),
      );
    }

    return {
      items: filtered,
      totalActiveSkus: all.length,
      categoryCounts,
    };
  }
}
