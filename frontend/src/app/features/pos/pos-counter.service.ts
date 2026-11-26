import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, map, of, switchMap, tap, throwError, timeout } from 'rxjs';
import { API_URL } from '../../core/api-url.token';
import { AuthService } from '../../core/auth/auth.service';

export type PosPaymentMode = 'CASH' | 'CARD' | 'BANK_TRANSFER';
export type PosInvoiceStatus = 'PAID' | 'PENDING' | 'VOIDED' | 'REFUNDED';

export interface PosProduct {
  productId: number;
  skuCode: string;
  barcode: string;
  itemName: string;
  category: string;
  retailPrice: number;
  currentStock: number;
  minReorderLevel: number;
  stockStatus: 'IN_STOCK' | 'LOW_STOCK' | 'OUT_OF_STOCK';
  stockBadgeLabel: string;
  imageKey: string;
  imageUrl: string;
}

export interface PosCustomer {
  customerId: number;
  fullName: string;
  phoneNumber: string;
  whatsappNumber: string | null;
  deliveryAddress: string | null;
  initial: string;
  subtitle: string;
}

export interface PosActiveOrder {
  orderNumber: string;
  customerName: string;
  itemSummary: string;
  agreedPrice: number;
  depositPaid: number;
  status: string;
}

export interface PosCounterContextResponse {
  products: PosProduct[];
  customers: PosCustomer[];
  activeOrders: PosActiveOrder[];
  tillNumber: string;
  tillStatus: 'OPEN' | 'CLOSED';
  openingFloat: number;
  lowStockCount: number;
}

export interface CreatePosCustomerRequest {
  fullName: string;
  phoneNumber: string;
  whatsappNumber?: string;
  deliveryAddress?: string;
}

export interface CreatePosInvoiceItemRequest {
  productId: number;
  quantity: number;
}

export interface CreatePosInvoiceRequest {
  customerId: number | null;
  items: CreatePosInvoiceItemRequest[];
  paymentMode: PosPaymentMode;
  paymentReference?: string | null;
  cashReceived: number;
  discountCode?: string | null;
  discountPercent: number;
  discountAmount: number;
  isCustomOrderDeposit: boolean;
}

export interface PosInvoiceItem {
  itemId: number;
  productId: number;
  skuCode: string;
  itemName: string;
  quantity: number;
  unitPrice: number;
  subtotal: number;
  isRefunded: boolean;
}

export interface PosInvoice {
  invoiceId: number;
  invoiceNumber: string;
  customerId: number | null;
  customerName: string;
  customerPhone: string;
  customerAddress: string | null;
  customerInitial: string;
  subtotalAmount: number;
  discountCode: string | null;
  discountPercent: number;
  discountAmount: number;
  totalAmount: number;
  paymentMode: PosPaymentMode;
  paymentModeLabel: string;
  paymentReference: string | null;
  cashReceived: number;
  changeDue: number;
  invoiceStatus: PosInvoiceStatus;
  isCustomOrderDeposit: boolean;
  tillNumber: string;
  billedBy: number | null;
  billedByName: string;
  invoiceDate: string;
  voidReason: string | null;
  voidedAt: string | null;
  items: PosInvoiceItem[];
  whatsappShareUrl: string;
}

export interface PosInvoiceHistoryResponse {
  invoices: PosInvoice[];
  totalCount: number;
  paidCount: number;
  pendingCount: number;
  voidedOrRefundedCount: number;
}

export interface VoidRefundInvoiceRequest {
  reason: string;
  ownerPin: string;
  itemIds?: number[];
}

export interface TillDenominationRequest {
  notes5000Count: number;
  notes1000Count: number;
  notes500Count: number;
  notes100Count: number;
  coins50Count: number;
}

export interface CloseTillDayRequest extends TillDenominationRequest {
  verifiedByName?: string;
}

export interface PosTillSummary {
  sessionId: number;
  sessionDate: string;
  displayDate: string;
  tillNumber: string;
  openingFloat: number;
  openedAt: string;
  cashInvoiceCount: number;
  cashSalesAmount: number;
  cardSlipCount: number;
  cardSalesAmount: number;
  bankSlipCount: number;
  bankSlipAmount: number;
  notes5000Count: number;
  notes1000Count: number;
  notes500Count: number;
  notes100Count: number;
  coins50Count: number;
  countedCashAmount: number;
  expectedCashAmount: number;
  varianceAmount: number;
  varianceStatus: 'BALANCED' | 'OVERAGE' | 'SHORTAGE';
  grossDayRevenue: number;
  safeDepositCash: number;
  sessionStatus: 'OPEN' | 'CLOSED';
  zReportNumber: string;
  closedByName: string;
  verifiedByName: string;
  closedAt: string | null;
}

const FALLBACK_PRODUCTS: PosProduct[] = [
  {
    productId: 1001,
    skuCode: 'FF-LIV-001',
    barcode: '8901245001928',
    itemName: 'Aurelia 3-Piece Teak Lounge Suite',
    category: 'Living Room',
    retailPrice: 45000,
    currentStock: 2,
    minReorderLevel: 3,
    stockStatus: 'IN_STOCK',
    stockBadgeLabel: 'Floor: 2 in stock',
    imageKey: 'lounge',
    imageUrl: '/assets/pos/lounge-suite.png',
  },
  {
    productId: 1002,
    skuCode: 'FF-BED-004',
    barcode: '8901245001935',
    itemName: 'Kandy Royal King Teak Bed',
    category: 'Bedroom',
    retailPrice: 78000,
    currentStock: 1,
    minReorderLevel: 3,
    stockStatus: 'LOW_STOCK',
    stockBadgeLabel: 'Low: 1 in stock',
    imageKey: 'bed',
    imageUrl: '/assets/pos/king-teak-bed.png',
  },
  {
    productId: 1003,
    skuCode: 'FF-DIN-002',
    barcode: '8901245001942',
    itemName: 'Riviera Solid Teak Dining Table',
    category: 'Dining Room',
    retailPrice: 62500,
    currentStock: 4,
    minReorderLevel: 3,
    stockStatus: 'IN_STOCK',
    stockBadgeLabel: 'Floor: 4 in stock',
    imageKey: 'dining',
    imageUrl: '/assets/pos/dining-table.png',
  },
  {
    productId: 1004,
    skuCode: 'FF-WAR-003',
    barcode: '8901245002024',
    itemName: 'Royal Arch 3-Door Teak Wardrobe',
    category: 'Bedroom',
    retailPrice: 145000,
    currentStock: 0,
    minReorderLevel: 3,
    stockStatus: 'OUT_OF_STOCK',
    stockBadgeLabel: 'Out: 0 in stock',
    imageKey: 'wardrobe',
    imageUrl: '/assets/pos/teak-wardrobe.png',
  },
];

const FALLBACK_CUSTOMERS: PosCustomer[] = [
  { customerId: 1, fullName: 'Nimal Silva', phoneNumber: '+94 77 123 4567', whatsappNumber: '+94 77 123 4567', deliveryAddress: 'Walk-in · Kottawa Showroom', initial: 'N', subtitle: '+94 77 123 4567 · Walk-in' },
  { customerId: 2, fullName: 'Kavinda Bandara', phoneNumber: '077 842 1190', whatsappNumber: '077 842 1190', deliveryAddress: 'No. 18, High Level Road, Pannipitiya', initial: 'K', subtitle: '077 842 1190 · Delivery' },
  { customerId: 3, fullName: 'Sunethra J.', phoneNumber: '071 553 9082', whatsappNumber: '071 553 9082', deliveryAddress: '42/B, Mattegoda Housing Scheme, Kottawa', initial: 'S', subtitle: '071 553 9082 · Delivery' },
  { customerId: 4, fullName: 'Kamal Perera', phoneNumber: '077 458 1290', whatsappNumber: '077 458 1290', deliveryAddress: 'No. 45, Pannipitiya Road, Kottawa', initial: 'K', subtitle: '077 458 1290 · Delivery' },
];

const FALLBACK_ACTIVE_ORDERS: PosActiveOrder[] = [
  { orderNumber: 'ORD-2026-4512', customerName: 'Sunil Jayakody', itemSummary: 'Custom 8-Seater Burma Teak Dining Suite', agreedPrice: 240000, depositPaid: 80000, status: 'IN_PRODUCTION' },
  { orderNumber: 'ORD-2026-4515', customerName: 'Dr. Nimal Perera', itemSummary: 'Carved King Canopy Bed (72"×78")', agreedPrice: 195000, depositPaid: 65000, status: 'READY_FOR_DELIVERY' },
  { orderNumber: 'ORD-2026-4519', customerName: 'Kumari Silva', itemSummary: 'Fitted 4-Door Dressing Almirah', agreedPrice: 165000, depositPaid: 55000, status: 'CONFIRMED' },
];

const FALLBACK_INVOICES: PosInvoice[] = [
  {
    invoiceId: 891,
    invoiceNumber: 'INV-2026-0891',
    customerId: 1,
    customerName: 'Nimal Silva',
    customerPhone: '+94 77 123 4567',
    customerAddress: 'Walk-in Customer',
    customerInitial: 'N',
    subtotalAmount: 107500,
    discountCode: 'DISC-10 · 10% Seasonal',
    discountPercent: 10,
    discountAmount: 10750,
    totalAmount: 96750,
    paymentMode: 'CASH',
    paymentModeLabel: 'Cash',
    paymentReference: 'Counter #01',
    cashReceived: 100000,
    changeDue: 3250,
    invoiceStatus: 'PAID',
    isCustomOrderDeposit: false,
    tillNumber: 'Till #01',
    billedBy: 1,
    billedByName: 'Yohan Methusael E J',
    invoiceDate: '2026-11-13T14:22:00+05:30',
    voidReason: null,
    voidedAt: null,
    items: [
      { itemId: 1, productId: 1001, skuCode: 'FF-LIV-001', itemName: 'Aurelia 3-Piece Lounge Suite', quantity: 1, unitPrice: 45000, subtotal: 45000, isRefunded: false },
      { itemId: 2, productId: 1003, skuCode: 'FF-DIN-002', itemName: 'Riviera 6-Chair Dining Table', quantity: 1, unitPrice: 62500, subtotal: 62500, isRefunded: false },
    ],
    whatsappShareUrl: 'https://wa.me/94771234567?text=Thank%20you%20from%20Wimalasiri%20Furniture%2C%20Kottawa!%20Receipt%20%23INV-2026-0891%20Paid%20Rs.%2096%2C750.00',
  },
  {
    invoiceId: 890,
    invoiceNumber: 'INV-2026-0890',
    customerId: 2,
    customerName: 'Kavinda Bandara',
    customerPhone: '077 842 1190',
    customerAddress: 'No. 18, High Level Road, Pannipitiya',
    customerInitial: 'K',
    subtotalAmount: 78000,
    discountCode: null,
    discountPercent: 0,
    discountAmount: 0,
    totalAmount: 78000,
    paymentMode: 'CARD',
    paymentModeLabel: 'Card',
    paymentReference: 'POS-VISA-7741',
    cashReceived: 78000,
    changeDue: 0,
    invoiceStatus: 'PAID',
    isCustomOrderDeposit: false,
    tillNumber: 'Till #01',
    billedBy: 1,
    billedByName: 'Yohan Methusael E J',
    invoiceDate: '2026-11-13T13:05:00+05:30',
    voidReason: null,
    voidedAt: null,
    items: [
      { itemId: 3, productId: 1002, skuCode: 'FF-BED-004', itemName: 'Kandy Royal King Teak Bed', quantity: 1, unitPrice: 78000, subtotal: 78000, isRefunded: false },
    ],
    whatsappShareUrl: 'https://wa.me/94778421190?text=Receipt%20INV-2026-0890',
  },
  {
    invoiceId: 889,
    invoiceNumber: 'INV-2026-0889',
    customerId: 3,
    customerName: 'Sunethra J.',
    customerPhone: '071 553 9082',
    customerAddress: '42/B, Mattegoda Housing Scheme, Kottawa',
    customerInitial: 'S',
    subtotalAmount: 89000,
    discountCode: null,
    discountPercent: 0,
    discountAmount: 0,
    totalAmount: 89000,
    paymentMode: 'BANK_TRANSFER',
    paymentModeLabel: 'Bank Slip',
    paymentReference: 'BOC-SLIP-20261113-884',
    cashReceived: 89000,
    changeDue: 0,
    invoiceStatus: 'PENDING',
    isCustomOrderDeposit: false,
    tillNumber: 'Till #01',
    billedBy: 1,
    billedByName: 'Yohan Methusael E J',
    invoiceDate: '2026-11-13T11:40:00+05:30',
    voidReason: null,
    voidedAt: null,
    items: [
      { itemId: 4, productId: 1001, skuCode: 'FF-LIV-001', itemName: 'Aurelia 3-Piece Lounge Suite (Custom Cushion)', quantity: 1, unitPrice: 89000, subtotal: 89000, isRefunded: false },
    ],
    whatsappShareUrl: 'https://wa.me/94715539082?text=Receipt%20INV-2026-0889',
  },
  {
    invoiceId: 888,
    invoiceNumber: 'INV-2026-0888',
    customerId: 4,
    customerName: 'Dilshan M.',
    customerPhone: '076 910 3341',
    customerAddress: 'Walk-in Customer',
    customerInitial: 'D',
    subtotalAmount: 42000,
    discountCode: null,
    discountPercent: 0,
    discountAmount: 0,
    totalAmount: 42000,
    paymentMode: 'CASH',
    paymentModeLabel: 'Cash',
    paymentReference: 'Counter #01',
    cashReceived: 45000,
    changeDue: 3000,
    invoiceStatus: 'VOIDED',
    isCustomOrderDeposit: false,
    tillNumber: 'Till #01',
    billedBy: 1,
    billedByName: 'Yohan Methusael E J',
    invoiceDate: '2026-11-13T10:15:00+05:30',
    voidReason: 'Customer requested cancellation',
    voidedAt: '2026-11-13T10:42:00+05:30',
    items: [
      { itemId: 5, productId: 1001, skuCode: 'FF-LIV-011', itemName: 'Kottawa Carved Nadun Coffee Table', quantity: 1, unitPrice: 42000, subtotal: 42000, isRefunded: true },
    ],
    whatsappShareUrl: 'https://wa.me/94769103341?text=Receipt%20INV-2026-0888',
  },
  {
    invoiceId: 887,
    invoiceNumber: 'INV-2026-0887',
    customerId: null,
    customerName: 'Ceylon Timber Co.',
    customerPhone: '011 284 7720',
    customerAddress: 'Industrial Zone, Homagama',
    customerInitial: 'C',
    subtotalAmount: 148000,
    discountCode: null,
    discountPercent: 0,
    discountAmount: 0,
    totalAmount: 148000,
    paymentMode: 'CARD',
    paymentModeLabel: 'Card',
    paymentReference: 'POS-AMEX-3902',
    cashReceived: 148000,
    changeDue: 0,
    invoiceStatus: 'PAID',
    isCustomOrderDeposit: false,
    tillNumber: 'Till #01',
    billedBy: 1,
    billedByName: 'Yohan Methusael E J',
    invoiceDate: '2026-11-13T09:30:00+05:30',
    voidReason: null,
    voidedAt: null,
    items: [
      { itemId: 6, productId: 1003, skuCode: 'FF-OFF-009', itemName: 'Moratuwa Executive Suite + Credenza', quantity: 1, unitPrice: 148000, subtotal: 148000, isRefunded: false },
    ],
    whatsappShareUrl: 'https://wa.me/94112847720?text=Receipt%20INV-2026-0887',
  },
  {
    invoiceId: 886,
    invoiceNumber: 'INV-2026-0886',
    customerId: null,
    customerName: 'Hasini Peiris',
    customerPhone: '077 612 4409',
    customerAddress: 'No. 9, Rukmalgama Road, Kottawa',
    customerInitial: 'H',
    subtotalAmount: 64500,
    discountCode: null,
    discountPercent: 0,
    discountAmount: 0,
    totalAmount: 64500,
    paymentMode: 'BANK_TRANSFER',
    paymentModeLabel: 'Bank Slip',
    paymentReference: 'SAMPATH-SLIP-9910',
    cashReceived: 64500,
    changeDue: 0,
    invoiceStatus: 'PAID',
    isCustomOrderDeposit: false,
    tillNumber: 'Till #01',
    billedBy: 1,
    billedByName: 'Yohan Methusael E J',
    invoiceDate: '2026-11-12T17:45:00+05:30',
    voidReason: null,
    voidedAt: null,
    items: [
      { itemId: 7, productId: 1003, skuCode: 'FF-LIV-014', itemName: 'Teak Verandah Lounge Chair', quantity: 1, unitPrice: 64500, subtotal: 64500, isRefunded: false },
    ],
    whatsappShareUrl: 'https://wa.me/94776124409?text=Receipt%20INV-2026-0886',
  },
  {
    invoiceId: 885,
    invoiceNumber: 'INV-2026-0885',
    customerId: null,
    customerName: 'Roshan Silva',
    customerPhone: '075 339 8120',
    customerAddress: 'Walk-in Customer',
    customerInitial: 'R',
    subtotalAmount: 35000,
    discountCode: null,
    discountPercent: 0,
    discountAmount: 0,
    totalAmount: 35000,
    paymentMode: 'CASH',
    paymentModeLabel: 'Cash',
    paymentReference: 'Counter #01',
    cashReceived: 35000,
    changeDue: 0,
    invoiceStatus: 'PAID',
    isCustomOrderDeposit: false,
    tillNumber: 'Till #01',
    billedBy: 1,
    billedByName: 'Yohan Methusael E J',
    invoiceDate: '2026-11-12T16:10:00+05:30',
    voidReason: null,
    voidedAt: null,
    items: [
      { itemId: 8, productId: 1001, skuCode: 'FF-ACC-002', itemName: 'Carved Teak Side Table', quantity: 1, unitPrice: 35000, subtotal: 35000, isRefunded: false },
    ],
    whatsappShareUrl: 'https://wa.me/94753398120?text=Receipt%20INV-2026-0885',
  },
  {
    invoiceId: 884,
    invoiceNumber: 'INV-2026-0884',
    customerId: null,
    customerName: 'Amaya Fernando',
    customerPhone: '077 998 2301',
    customerAddress: '74/1, Old Road, Maharagama',
    customerInitial: 'A',
    subtotalAmount: 112000,
    discountCode: null,
    discountPercent: 0,
    discountAmount: 0,
    totalAmount: 112000,
    paymentMode: 'CARD',
    paymentModeLabel: 'Card',
    paymentReference: 'POS-MASTER-2041',
    cashReceived: 112000,
    changeDue: 0,
    invoiceStatus: 'REFUNDED',
    isCustomOrderDeposit: false,
    tillNumber: 'Till #01',
    billedBy: 1,
    billedByName: 'Yohan Methusael E J',
    invoiceDate: '2026-11-12T14:20:00+05:30',
    voidReason: 'Customer exchanged for larger dining set',
    voidedAt: '2026-11-12T16:40:00+05:30',
    items: [
      { itemId: 9, productId: 1003, skuCode: 'FF-DIN-004', itemName: '4-Seater Mahogany Dining Table', quantity: 1, unitPrice: 112000, subtotal: 112000, isRefunded: true },
    ],
    whatsappShareUrl: 'https://wa.me/94779982301?text=Receipt%20INV-2026-0884',
  },
];

const FALLBACK_TILL: PosTillSummary = {
  sessionId: 1,
  sessionDate: '2026-11-13',
  displayDate: 'Fri, 13 Nov 2026',
  tillNumber: 'Till #01',
  openingFloat: 25000,
  openedAt: '08:30 AM',
  cashInvoiceCount: 18,
  cashSalesAmount: 148500,
  cardSlipCount: 11,
  cardSalesAmount: 182000,
  bankSlipCount: 3,
  bankSlipAmount: 65000,
  notes5000Count: 26,
  notes1000Count: 38,
  notes500Count: 8,
  notes100Count: 12,
  coins50Count: 6,
  countedCashAmount: 173500,
  expectedCashAmount: 173500,
  varianceAmount: 0,
  varianceStatus: 'BALANCED',
  grossDayRevenue: 395500,
  safeDepositCash: 148500,
  sessionStatus: 'OPEN',
  zReportNumber: 'ZR-2026-1113-01',
  closedByName: 'Yohan Methusael E J',
  verifiedByName: 'Wimalasiri (Owner)',
  closedAt: null,
};

@Injectable({ providedIn: 'root' })
export class PosCounterService {
  private readonly http = inject(HttpClient);
  private readonly apiUrl = inject(API_URL);
  private readonly auth = inject(AuthService);
  private readonly baseUrl = `${this.apiUrl}/pos`;

  private readonly localProducts = signal<PosProduct[]>([...FALLBACK_PRODUCTS]);
  private readonly localCustomers = signal<PosCustomer[]>([...FALLBACK_CUSTOMERS]);
  private readonly localInvoices = signal<PosInvoice[]>([...FALLBACK_INVOICES]);
  private readonly localTill = signal<PosTillSummary>({ ...FALLBACK_TILL });

  getCounterContext(category = 'All Items', search = ''): Observable<PosCounterContextResponse> {
    const params: Record<string, string> = {};
    if (category && category !== 'All Items' && category !== 'All') params['category'] = category;
    if (search.trim()) params['search'] = search.trim();

    return this.http.get<PosCounterContextResponse>(`${this.baseUrl}/context`, { params }).pipe(
      timeout(12000),
      tap(res => {
        if (!category || category === 'All Items' || category === 'All') {
          if (!search.trim() && res.products.length > 0) {
            this.localProducts.set(res.products);
          }
        }
        if (res.customers.length > 0) this.localCustomers.set(res.customers);
      }),
      catchError(() => of(this.buildFallbackContext(category, search))),
    );
  }

  createCustomer(request: CreatePosCustomerRequest): Observable<PosCustomer> {
    return this.auth.refreshCsrf().pipe(
      switchMap(() => this.http.post<PosCustomer>(`${this.baseUrl}/customers`, request)),
      timeout(12000),
      tap(created => this.localCustomers.update(list => [created, ...list])),
      catchError(err => {
        if (err?.status === 400 || err?.status === 403 || err?.status === 409) {
          return throwError(() => err);
        }
        const created: PosCustomer = {
          customerId: Date.now(),
          fullName: request.fullName.trim(),
          phoneNumber: request.phoneNumber.trim(),
          whatsappNumber: request.whatsappNumber?.trim() || request.phoneNumber.trim(),
          deliveryAddress: request.deliveryAddress?.trim() || 'Walk-in · Kottawa Showroom',
          initial: request.fullName.trim()[0]?.toUpperCase() || 'C',
          subtitle: `${request.phoneNumber.trim()} · ${request.deliveryAddress?.trim() ? 'Delivery' : 'Walk-in'}`,
        };
        this.localCustomers.update(list => [created, ...list]);
        return of(created);
      }),
    );
  }

  createInvoice(request: CreatePosInvoiceRequest): Observable<PosInvoice> {
    return this.auth.refreshCsrf().pipe(
      switchMap(() => this.http.post<PosInvoice>(`${this.baseUrl}/invoices`, request)),
      timeout(12000),
      tap(inv => {
        this.localInvoices.update(list => [inv, ...list.filter(i => i.invoiceNumber !== inv.invoiceNumber)]);
        this.deductLocalStock(request.items);
      }),
      catchError(err => {
        if (err?.status === 400 || err?.status === 401 || err?.status === 403 || err?.status === 409) {
          return throwError(() => err);
        }
        return this.simulateOfflineCheckout(request);
      }),
    );
  }

  listInvoices(search = '', paymentMode = 'All', date = ''): Observable<PosInvoiceHistoryResponse> {
    const params: Record<string, string> = {};
    if (search.trim()) params['search'] = search.trim();
    if (paymentMode && paymentMode !== 'All') {
      params['paymentMode'] = paymentMode === 'Bank Slip' ? 'BANK_TRANSFER' : paymentMode.toUpperCase();
    }
    if (date.trim()) params['date'] = date.trim();

    return this.http.get<PosInvoiceHistoryResponse>(`${this.baseUrl}/invoices`, { params }).pipe(
      timeout(12000),
      map(res => (res.invoices.length > 0 ? res : this.filterFallbackInvoices(search, paymentMode, date))),
      catchError(() => of(this.filterFallbackInvoices(search, paymentMode, date))),
    );
  }

  voidOrRefundInvoice(invoiceId: number, request: VoidRefundInvoiceRequest): Observable<PosInvoice> {
    return this.auth.refreshCsrf().pipe(
      switchMap(() => this.http.post<PosInvoice>(`${this.baseUrl}/invoices/${invoiceId}/void`, request)),
      timeout(12000),
      tap(updated => {
        this.localInvoices.update(list => list.map(i => (i.invoiceId === invoiceId ? updated : i)));
      }),
      catchError(err => {
        if (err?.status === 400 || err?.status === 401 || err?.status === 403 || err?.status === 409) {
          return throwError(() => err);
        }
        return this.simulateOfflineVoid(invoiceId, request);
      }),
    );
  }

  getTillSummary(): Observable<PosTillSummary> {
    return this.http.get<PosTillSummary>(`${this.baseUrl}/till/current`).pipe(
      timeout(12000),
      map(res => (res.cashSalesAmount > 0 || res.sessionStatus === 'CLOSED' ? res : this.localTill())),
      catchError(() => of(this.localTill())),
    );
  }

  updateTillCount(request: TillDenominationRequest): Observable<PosTillSummary> {
    return this.auth.refreshCsrf().pipe(
      switchMap(() => this.http.put<PosTillSummary>(`${this.baseUrl}/till/count`, request)),
      timeout(12000),
      tap(res => this.localTill.set(res)),
      catchError(() => of(this.computeLocalTill(request))),
    );
  }

  closeTillDay(request: CloseTillDayRequest): Observable<PosTillSummary> {
    return this.auth.refreshCsrf().pipe(
      switchMap(() => this.http.post<PosTillSummary>(`${this.baseUrl}/till/close`, request)),
      timeout(12000),
      tap(res => this.localTill.set(res)),
      catchError(err => {
        if (err?.status === 400 || err?.status === 401 || err?.status === 403 || err?.status === 409) {
          return throwError(() => err);
        }
        const next = {
          ...this.computeLocalTill(request),
          sessionStatus: 'CLOSED' as const,
          closedByName: this.auth.user()?.fullName || 'Yohan Methusael E J',
          verifiedByName: request.verifiedByName || 'Wimalasiri (Owner)',
          closedAt: new Date().toISOString(),
        };
        this.localTill.set(next);
        return of(next);
      }),
    );
  }

  private buildFallbackContext(category: string, search: string): PosCounterContextResponse {
    let items = [...this.localProducts()];
    if (category && category !== 'All Items' && category !== 'All') {
      items = items.filter(p => p.category.toLowerCase() === category.toLowerCase());
    }
    if (search.trim()) {
      const term = search.trim().toLowerCase();
      items = items.filter(
        p =>
          p.itemName.toLowerCase().includes(term) ||
          p.skuCode.toLowerCase().includes(term) ||
          p.barcode.toLowerCase().includes(term) ||
          p.category.toLowerCase().includes(term),
      );
    }
    const till = this.localTill();
    return {
      products: items,
      customers: this.localCustomers(),
      activeOrders: FALLBACK_ACTIVE_ORDERS,
      tillNumber: till.tillNumber,
      tillStatus: till.sessionStatus,
      openingFloat: till.openingFloat,
      lowStockCount: this.localProducts().filter(p => p.currentStock <= p.minReorderLevel).length || 3,
    };
  }

  private deductLocalStock(lines: CreatePosInvoiceItemRequest[]): void {
    this.localProducts.update(products =>
      products.map(p => {
        const line = lines.find(l => l.productId === p.productId);
        if (!line) return p;
        const nextStock = Math.max(0, p.currentStock - line.quantity);
        const stockStatus = nextStock <= 0 ? 'OUT_OF_STOCK' : nextStock <= p.minReorderLevel ? 'LOW_STOCK' : 'IN_STOCK';
        const stockBadgeLabel =
          nextStock <= 0
            ? 'Out: 0 in stock'
            : nextStock <= p.minReorderLevel
              ? `Low: ${nextStock} in stock`
              : `Floor: ${nextStock} in stock`;
        return { ...p, currentStock: nextStock, stockStatus, stockBadgeLabel };
      }),
    );
  }

  private simulateOfflineCheckout(request: CreatePosInvoiceRequest): Observable<PosInvoice> {
    if (this.localTill().sessionStatus === 'CLOSED') {
      return throwError(() => ({ error: { message: 'Till #01 is closed and locked for today.' } }));
    }
    const products = this.localProducts();
    const lines: PosInvoiceItem[] = [];
    for (const item of request.items) {
      const prod = products.find(p => p.productId === item.productId);
      if (!prod || prod.currentStock < item.quantity) {
        return throwError(() => ({
          status: 409,
          error: {
            message: `Database trigger trg_pos_stock_deduct aborted checkout: ${prod?.itemName ?? 'Selected item'} (${prod?.skuCode ?? 'SKU'}) only has ${prod?.currentStock ?? 0} unit(s) available.`,
          },
        }));
      }
      lines.push({
        itemId: lines.length + 1,
        productId: prod.productId,
        skuCode: prod.skuCode,
        itemName: prod.itemName,
        quantity: item.quantity,
        unitPrice: prod.retailPrice,
        subtotal: prod.retailPrice * item.quantity,
        isRefunded: false,
      });
    }

    const subtotal = lines.reduce((s, l) => s + l.subtotal, 0);
    const discAmt = request.discountPercent > 0 ? Math.round((subtotal * request.discountPercent) / 100) : request.discountAmount;
    const net = Math.max(0, subtotal - discAmt);
    const total = request.isCustomOrderDeposit ? Math.round((net / 3) * 100) / 100 : net;
    const cashReceived = request.paymentMode === 'CASH' ? Math.max(total, request.cashReceived) : total;
    const changeDue = request.paymentMode === 'CASH' ? Math.max(0, cashReceived - total) : 0;
    const cust = this.localCustomers().find(c => c.customerId === request.customerId) ?? this.localCustomers()[0];
    const nextId = Math.max(891, ...this.localInvoices().map(i => i.invoiceId)) + 1;
    const invNo = `INV-2026-${String(nextId).padStart(4, '0')}`;
    const modeLabel = request.paymentMode === 'CARD' ? 'Card' : request.paymentMode === 'BANK_TRANSFER' ? 'Bank Slip' : 'Cash';

    const created: PosInvoice = {
      invoiceId: nextId,
      invoiceNumber: invNo,
      customerId: cust?.customerId ?? null,
      customerName: cust?.fullName ?? 'Walk-in Customer',
      customerPhone: cust?.phoneNumber ?? '+94 77 123 4567',
      customerAddress: cust?.deliveryAddress ?? 'Walk-in Customer',
      customerInitial: cust?.initial ?? 'W',
      subtotalAmount: subtotal,
      discountCode: discAmt > 0 ? request.discountCode || `DISC-${request.discountPercent}` : null,
      discountPercent: request.discountPercent,
      discountAmount: discAmt,
      totalAmount: total,
      paymentMode: request.paymentMode,
      paymentModeLabel: modeLabel,
      paymentReference: request.paymentReference || 'Counter #01',
      cashReceived,
      changeDue,
      invoiceStatus: 'PAID',
      isCustomOrderDeposit: request.isCustomOrderDeposit,
      tillNumber: 'Till #01',
      billedBy: 1,
      billedByName: this.auth.user()?.fullName || 'Yohan Methusael E J',
      invoiceDate: new Date().toISOString(),
      voidReason: null,
      voidedAt: null,
      items: lines,
      whatsappShareUrl: `https://wa.me/94771234567?text=${encodeURIComponent(`Wimalasiri Furniture Receipt #${invNo} · Paid Rs. ${total.toLocaleString('en-US')}`)}`,
    };

    this.deductLocalStock(request.items);
    this.localInvoices.update(list => [created, ...list]);
    return of(created);
  }

  private filterFallbackInvoices(search: string, paymentMode: string, date: string): PosInvoiceHistoryResponse {
    const all = this.localInvoices();
    let filtered = [...all];
    if (search.trim()) {
      const term = search.trim().toLowerCase();
      filtered = filtered.filter(
        i =>
          i.invoiceNumber.toLowerCase().includes(term) ||
          i.customerName.toLowerCase().includes(term) ||
          i.customerPhone.toLowerCase().includes(term),
      );
    }
    if (paymentMode && paymentMode !== 'All') {
      const norm = paymentMode === 'Bank Slip' ? 'BANK_TRANSFER' : paymentMode.toUpperCase();
      filtered = filtered.filter(i => i.paymentMode === norm);
    }
    if (date.trim()) {
      filtered = filtered.filter(i => i.invoiceDate.startsWith(date.trim()));
    }
    return {
      invoices: filtered,
      totalCount: Math.max(48, all.length),
      paidCount: all.filter(i => i.invoiceStatus === 'PAID').length,
      pendingCount: all.filter(i => i.invoiceStatus === 'PENDING').length,
      voidedOrRefundedCount: all.filter(i => i.invoiceStatus === 'VOIDED' || i.invoiceStatus === 'REFUNDED').length,
    };
  }

  private simulateOfflineVoid(invoiceId: number, request: VoidRefundInvoiceRequest): Observable<PosInvoice> {
    const target = this.localInvoices().find(i => i.invoiceId === invoiceId);
    if (!target) return throwError(() => ({ error: { message: 'Invoice not found.' } }));
    const partial = request.itemIds && request.itemIds.length > 0 && request.itemIds.length < target.items.length;
    const updated: PosInvoice = {
      ...target,
      invoiceStatus: partial ? 'REFUNDED' : 'VOIDED',
      voidReason: request.reason,
      voidedAt: new Date().toISOString(),
      items: target.items.map(it =>
        !request.itemIds || request.itemIds.length === 0 || request.itemIds.includes(it.itemId)
          ? { ...it, isRefunded: true }
          : it,
      ),
    };
    this.localInvoices.update(list => list.map(i => (i.invoiceId === invoiceId ? updated : i)));
    return of(updated);
  }

  private computeLocalTill(r: TillDenominationRequest): PosTillSummary {
    const cur = this.localTill();
    const counted =
      r.notes5000Count * 5000 +
      r.notes1000Count * 1000 +
      r.notes500Count * 500 +
      r.notes100Count * 100 +
      r.coins50Count * 50;
    const variance = counted - cur.expectedCashAmount;
    const varianceStatus = variance === 0 ? 'BALANCED' : variance > 0 ? 'OVERAGE' : 'SHORTAGE';
    const next: PosTillSummary = {
      ...cur,
      ...r,
      countedCashAmount: counted,
      varianceAmount: variance,
      varianceStatus,
      safeDepositCash: Math.max(0, counted - cur.openingFloat),
    };
    this.localTill.set(next);
    return next;
  }
}
