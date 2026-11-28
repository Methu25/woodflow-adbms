import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { PermissionsService } from '../../core/auth/permissions.service';
import {
  PosActiveOrder,
  PosCounterService,
  PosCustomer,
  PosInvoice,
  PosPaymentMode,
  PosProduct,
} from './pos-counter.service';

export interface PosCartItem {
  product: PosProduct;
  quantity: number;
}

@Component({
  selector: 'app-pos-counter',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, RouterLinkActive],
  templateUrl: './pos-counter.component.html',
  styleUrls: [
    './pos-header.scss',
    './pos-dialogs.scss',
    './pos-counter.component.scss',
    './pos-counter-bill.scss',
    './pos-counter-pay.scss',
  ],
})
export class PosCounterComponent {
  private readonly posService = inject(PosCounterService);
  private readonly destroyRef = inject(DestroyRef);
  readonly auth = inject(AuthService);
  readonly permissions = inject(PermissionsService);

  readonly categories = ['All Items', 'Living Room', 'Bedroom', 'Dining Room'];
  readonly selectedCategory = signal<string>('All Items');
  readonly searchQuery = signal<string>('');

  readonly products = signal<PosProduct[]>([]);
  readonly customers = signal<PosCustomer[]>([]);
  readonly activeOrders = signal<PosActiveOrder[]>([]);
  readonly tillNumber = signal<string>('Till #01');
  readonly tillStatus = signal<'OPEN' | 'CLOSED'>('OPEN');
  readonly lowStockCount = signal<number>(3);

  readonly loading = signal<boolean>(false);
  readonly loadError = signal<string>('');
  readonly submitting = signal<boolean>(false);

  readonly rightTab = signal<'BILL' | 'ORDERS'>('BILL');
  readonly selectedCustomer = signal<PosCustomer | null>(null);
  readonly cart = signal<PosCartItem[]>([]);
  private initialCartSeeded = false;

  readonly customOrderDepositMode = signal<boolean>(false);
  readonly discountPreset = signal<number>(10);
  readonly paymentMode = signal<PosPaymentMode>('CASH');
  readonly paymentReference = signal<string>('');
  readonly cashReceivedInput = signal<number>(100000);

  // Drawers, Dialogs & States matching Figma sub-screens
  readonly showAddCustomerDrawer = signal<boolean>(false);
  readonly newCustomerName = signal<string>('');
  readonly newCustomerPhone = signal<string>('');
  readonly newCustomerWhatsapp = signal<string>('');
  readonly newCustomerAddress = signal<string>('');
  readonly customerFormError = signal<string>('');

  readonly showPaymentRefDialog = signal<boolean>(false);
  readonly tempPaymentRef = signal<string>('BOC-SLIP-20261113-884');
  readonly paymentRefError = signal<string>('');

  readonly showClearBillDialog = signal<boolean>(false);
  readonly outOfStockProduct = signal<PosProduct | null>(null);
  readonly checkoutFailedMessage = signal<string>('');
  readonly lastCompletedInvoice = signal<PosInvoice | null>(null);
  readonly showReceiptDialog = signal<boolean>(false);

  readonly navTabs = [
    { label: '01 Counter POS / Billing', route: '/pos', exact: true },
    { label: '02 Invoice History & Reprint', route: '/pos/invoices', exact: false },
    { label: '03 Till Closing', route: '/pos/till-closing', exact: false },
  ];

  readonly isOwner = computed(() => this.auth.user()?.role === 'OWNER');
  readonly cashierName = computed(() => this.auth.user()?.fullName || 'Yohan Methusael E J');
  readonly cashierRole = computed(() => (this.isOwner() ? 'Owner · Cashier' : 'Cashier'));

  readonly totalCartUnits = computed(() => this.cart().reduce((sum, c) => sum + c.quantity, 0));
  readonly subtotal = computed(() =>
    this.cart().reduce((sum, c) => sum + c.product.retailPrice * c.quantity, 0),
  );
  readonly effectiveDiscountPercent = computed(() => (this.isOwner() ? this.discountPreset() : 0));
  readonly discountAmount = computed(() =>
    Math.round((this.subtotal() * this.effectiveDiscountPercent()) / 100),
  );
  readonly netAfterDiscount = computed(() => Math.max(0, this.subtotal() - this.discountAmount()));
  readonly totalPayable = computed(() =>
    this.customOrderDepositMode()
      ? Math.round((this.netAfterDiscount() / 3) * 100) / 100
      : this.netAfterDiscount(),
  );
  readonly changeDue = computed(() =>
    this.paymentMode() === 'CASH' ? Math.max(0, this.cashReceivedInput() - this.totalPayable()) : 0,
  );

  constructor() {
    this.permissions.refresh().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => {} });
    this.loadCounter();
  }

  loadCounter(): void {
    this.loading.set(true);
    this.loadError.set('');
    this.posService
      .getCounterContext(this.selectedCategory(), this.searchQuery())
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: res => {
          this.products.set(res.products);
          this.customers.set(res.customers);
          this.activeOrders.set(res.activeOrders);
          this.tillNumber.set(res.tillNumber || 'Till #01');
          this.tillStatus.set(res.tillStatus || 'OPEN');
          this.lowStockCount.set(res.lowStockCount || 3);

          if (!this.selectedCustomer() && res.customers.length > 0) {
            const nimal = res.customers.find(c => c.fullName.toLowerCase().includes('nimal')) ?? res.customers[0];
            this.selectedCustomer.set(nimal);
          }

          if (!this.initialCartSeeded && res.products.length > 0) {
            this.initialCartSeeded = true;
            const lounge = res.products.find(p => p.skuCode === 'FF-LIV-001' && p.currentStock > 0);
            const dining = res.products.find(p => p.skuCode === 'FF-DIN-002' && p.currentStock > 0);
            const seeded: PosCartItem[] = [];
            if (lounge) seeded.push({ product: lounge, quantity: 1 });
            if (dining) seeded.push({ product: dining, quantity: 1 });
            if (seeded.length > 0) {
              this.cart.set(seeded);
              const sub = seeded.reduce((s, x) => s + x.product.retailPrice * x.quantity, 0);
              const estTotal = this.isOwner() ? Math.round(sub * 0.9) : sub;
              this.cashReceivedInput.set(Math.max(100000, Math.ceil(estTotal / 5000) * 5000));
            }
          }
        },
        error: () => {
          this.loadError.set(
            'Unable to reach PosCounterController API (/api/pos/context). Check PostgreSQL service status.',
          );
        },
      });
  }

  selectCategory(cat: string): void {
    if (this.selectedCategory() === cat) return;
    this.selectedCategory.set(cat);
    this.loadCounter();
  }

  onSearchChange(val: string): void {
    this.searchQuery.set(val);
    this.loadCounter();
  }

  addToBill(item: PosProduct): void {
    this.checkoutFailedMessage.set('');
    if (item.currentStock <= 0 || item.stockStatus === 'OUT_OF_STOCK') {
      this.outOfStockProduct.set(item);
      return;
    }

    const existing = this.cart().find(c => c.product.productId === item.productId);
    if (existing && existing.quantity >= item.currentStock) {
      this.checkoutFailedMessage.set(
        `Database trigger trg_pos_stock_deduct limit: ${item.itemName} (${item.skuCode}) only has ${item.currentStock} unit(s) on the floor.`,
      );
      return;
    }

    if (existing) {
      this.cart.update(list =>
        list.map(c =>
          c.product.productId === item.productId ? { ...c, quantity: c.quantity + 1 } : c,
        ),
      );
    } else {
      this.cart.update(list => [...list, { product: item, quantity: 1 }]);
    }

    if (this.cashReceivedInput() < this.totalPayable()) {
      this.cashReceivedInput.set(Math.ceil(this.totalPayable() / 1000) * 1000);
    }
  }

  focusFirstInStockItem(): void {
    const first = this.products().find(p => p.currentStock > 0);
    if (first) this.addToBill(first);
  }

  changeQty(productId: number, delta: number): void {
    const entry = this.cart().find(c => c.product.productId === productId);
    if (!entry) return;
    const next = entry.quantity + delta;
    if (next <= 0) {
      this.removeItem(productId);
      return;
    }
    if (next > entry.product.currentStock) {
      this.checkoutFailedMessage.set(
        `${entry.product.itemName} (${entry.product.skuCode}) only has ${entry.product.currentStock} unit(s) in stock.`,
      );
      return;
    }
    this.checkoutFailedMessage.set('');
    this.cart.update(list =>
      list.map(c => (c.product.productId === productId ? { ...c, quantity: next } : c)),
    );
  }

  removeItem(productId: number): void {
    this.cart.update(list => list.filter(c => c.product.productId !== productId));
  }

  confirmClearBill(): void {
    this.cart.set([]);
    this.showClearBillDialog.set(false);
    this.checkoutFailedMessage.set('');
  }

  cycleCustomer(): void {
    const list = this.customers();
    if (list.length <= 1) return;
    const curId = this.selectedCustomer()?.customerId;
    const idx = list.findIndex(c => c.customerId === curId);
    this.selectedCustomer.set(list[(idx + 1) % list.length]);
  }

  openAddCustomerDrawer(): void {
    this.customerFormError.set('');
    this.newCustomerName.set('');
    this.newCustomerPhone.set('');
    this.newCustomerWhatsapp.set('');
    this.newCustomerAddress.set('');
    this.showAddCustomerDrawer.set(true);
  }

  saveNewCustomer(): void {
    const fullName = this.newCustomerName().trim();
    const phoneNumber = this.newCustomerPhone().trim();
    if (fullName.length < 2 || phoneNumber.length < 7) {
      this.customerFormError.set('Enter customer full name and a valid phone number.');
      return;
    }

    this.submitting.set(true);
    this.customerFormError.set('');
    this.posService
      .createCustomer({
        fullName,
        phoneNumber,
        whatsappNumber: this.newCustomerWhatsapp().trim() || phoneNumber,
        deliveryAddress: this.newCustomerAddress().trim() || 'Walk-in · Kottawa Showroom',
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.submitting.set(false)),
      )
      .subscribe({
        next: created => {
          this.customers.update(list => [created, ...list]);
          this.selectedCustomer.set(created);
          this.showAddCustomerDrawer.set(false);
        },
        error: err => {
          this.customerFormError.set(err?.error?.message || 'Unable to save customer.');
        },
      });
  }

  selectPaymentMode(mode: PosPaymentMode): void {
    this.paymentMode.set(mode);
    if (mode !== 'CASH') {
      this.paymentRefError.set('');
      this.tempPaymentRef.set(
        this.paymentReference() ||
          (mode === 'CARD' ? 'POS-VISA-8841' : 'BOC-SLIP-20261113-884'),
      );
      this.showPaymentRefDialog.set(true);
    }
  }

  confirmPaymentReference(): void {
    const ref = this.tempPaymentRef().trim();
    if (ref.length < 4) {
      this.paymentRefError.set('Enter a valid reference or approval number (minimum 4 characters).');
      return;
    }
    this.paymentReference.set(ref);
    this.showPaymentRefDialog.set(false);
  }

  checkoutAndPrint(): void {
    if (this.cart().length === 0 || this.submitting()) return;
    if (this.tillStatus() === 'CLOSED') {
      this.checkoutFailedMessage.set(
        'Till #01 is closed and locked for today. All POS checkouts are disabled until tomorrow 08:30 AM.',
      );
      return;
    }
    if (this.paymentMode() === 'CASH' && this.cashReceivedInput() < this.totalPayable()) {
      this.checkoutFailedMessage.set(
        `Cash received (${this.formatCurrency(this.cashReceivedInput())}) is below Total Payable (${this.formatCurrency(this.totalPayable())}).`,
      );
      return;
    }
    if (this.paymentMode() !== 'CASH' && !this.paymentReference().trim()) {
      this.selectPaymentMode(this.paymentMode());
      return;
    }

    this.submitting.set(true);
    this.checkoutFailedMessage.set('');
    const discPct = this.effectiveDiscountPercent();

    this.posService
      .createInvoice({
        customerId: this.selectedCustomer()?.customerId ?? null,
        items: this.cart().map(c => ({ productId: c.product.productId, quantity: c.quantity })),
        paymentMode: this.paymentMode(),
        paymentReference: this.paymentMode() === 'CASH' ? 'Counter #01' : this.paymentReference(),
        cashReceived: this.paymentMode() === 'CASH' ? this.cashReceivedInput() : this.totalPayable(),
        discountCode: discPct > 0 ? `DISC-${discPct} · ${discPct}% Seasonal` : null,
        discountPercent: discPct,
        discountAmount: this.discountAmount(),
        isCustomOrderDeposit: this.customOrderDepositMode(),
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.submitting.set(false)),
      )
      .subscribe({
        next: invoice => {
          this.lastCompletedInvoice.set(invoice);
          this.showReceiptDialog.set(true);
          this.cart.set([]);
          this.loadCounter();
        },
        error: err => {
          this.checkoutFailedMessage.set(
            err?.error?.message ||
              'Database trigger trg_pos_stock_deduct aborted checkout due to insufficient showroom stock.',
          );
        },
      });
  }

  printReceipt(): void {
    window.print();
  }

  downloadReceiptJson(): void {
    const inv = this.lastCompletedInvoice();
    if (!inv) return;
    const lines = [
      `WIMALASIRI FURNITURE · KOTTAWA SHOWROOM`,
      `Invoice: ${inv.invoiceNumber}  |  Date: ${new Date(inv.invoiceDate).toLocaleString()}`,
      `Customer: ${inv.customerName} (${inv.customerPhone})`,
      `Cashier: ${inv.billedByName} (${inv.tillNumber})`,
      `--------------------------------------------------`,
      ...inv.items.map(i => `${i.quantity}x ${i.itemName} (${i.skuCode}) - Rs. ${i.subtotal.toLocaleString('en-US', { minimumFractionDigits: 2 })}`),
      `--------------------------------------------------`,
      `Subtotal: Rs. ${inv.subtotalAmount.toLocaleString('en-US', { minimumFractionDigits: 2 })}`,
      `Discount: -Rs. ${inv.discountAmount.toLocaleString('en-US', { minimumFractionDigits: 2 })}`,
      `Total Payable: Rs. ${inv.totalAmount.toLocaleString('en-US', { minimumFractionDigits: 2 })}`,
      `Payment: ${inv.paymentModeLabel} (Received Rs. ${inv.cashReceived.toLocaleString('en-US', { minimumFractionDigits: 2 })} | Change Rs. ${inv.changeDue.toLocaleString('en-US', { minimumFractionDigits: 2 })})`,
    ].join('\n');

    const blob = new Blob([lines], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${inv.invoiceNumber}-receipt.txt`;
    a.click();
    URL.revokeObjectURL(url);
  }

  formatCurrency(amount: number): string {
    return (
      'Rs. ' +
      (amount ?? 0).toLocaleString('en-US', {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      })
    );
  }

  formatWhole(amount: number): string {
    return 'Rs. ' + Math.round(amount ?? 0).toLocaleString('en-US');
  }

  stockClass(item: PosProduct): string {
    if (item.currentStock <= 0 || item.stockStatus === 'OUT_OF_STOCK') return 'stock-out';
    if (item.stockStatus === 'LOW_STOCK' || item.currentStock <= item.minReorderLevel) return 'stock-low';
    return 'stock-in';
  }
}
