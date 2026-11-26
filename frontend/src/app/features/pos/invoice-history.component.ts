import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { PermissionsService } from '../../core/auth/permissions.service';
import { PosCounterService, PosInvoice } from './pos-counter.service';

@Component({
  selector: 'app-invoice-history',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, RouterLinkActive],
  templateUrl: './invoice-history.component.html',
  styleUrls: [
    './pos-header.scss',
    './pos-dialogs.scss',
    './invoice-history.component.scss',
    './invoice-history-detail.scss',
  ],
})
export class InvoiceHistoryComponent {
  private readonly posService = inject(PosCounterService);
  private readonly destroyRef = inject(DestroyRef);
  readonly auth = inject(AuthService);
  readonly permissions = inject(PermissionsService);

  readonly navTabs = [
    { label: '01 Counter POS / Billing', route: '/pos', exact: true },
    { label: '02 Invoice History & Reprint', route: '/pos/invoices', exact: false },
    { label: '03 Till Closing', route: '/pos/till-closing', exact: false },
  ];

  readonly paymentFilters = ['All', 'Cash', 'Card', 'Bank Slip'];
  readonly searchQuery = signal<string>('');
  readonly selectedPayment = signal<string>('All');
  readonly filterTodayOnly = signal<boolean>(false);
  readonly currentPage = signal<number>(1);

  readonly invoices = signal<PosInvoice[]>([]);
  readonly totalCount = signal<number>(48);
  readonly selectedInvoice = signal<PosInvoice | null>(null);
  readonly loading = signal<boolean>(false);
  readonly submitting = signal<boolean>(false);
  readonly feedbackMessage = signal<string>('');
  readonly errorMessage = signal<string>('');

  // Inline Quick Void inputs (Screen 02 right card)
  readonly inlinePin = signal<string>('1234');
  readonly inlineReason = signal<string>('Customer requested cancellation');

  // Figma 196:470 Drawer & 196:512 Multi-Step Void/Refund & 196:417 Receipt Dialog
  readonly showDetailDrawer = signal<boolean>(false);
  readonly showReprintDialog = signal<boolean>(false);
  readonly showMultiStepVoidModal = signal<boolean>(false);
  readonly selectedVoidItemIds = signal<number[]>([]);
  readonly modalVoidReason = signal<string>('Customer requested exchange for Kandy King Teak Bed');
  readonly modalOwnerPin = signal<string>('1234');

  readonly isOwner = computed(() => this.auth.user()?.role === 'OWNER');
  readonly cashierName = computed(() => this.auth.user()?.fullName || 'Yohan Methusael E J');
  readonly cashierRole = computed(() => (this.isOwner() ? 'Owner · Cashier' : 'Cashier'));

  readonly pagedInvoices = computed(() => {
    const start = (this.currentPage() - 1) * 8;
    return this.invoices().slice(start, start + 8);
  });

  readonly selectedUnitsCount = computed(() =>
    (this.selectedInvoice()?.items ?? []).reduce((sum, i) => sum + i.quantity, 0),
  );

  readonly multiStepRefundAmount = computed(() => {
    const inv = this.selectedInvoice();
    if (!inv) return 0;
    const ids = this.selectedVoidItemIds();
    const chosen = inv.items.filter(i => ids.includes(i.itemId));
    if (chosen.length === 0) return 0;
    if (chosen.length === inv.items.length) return inv.totalAmount;
    const ratio = inv.subtotalAmount > 0 ? chosen.reduce((s, i) => s + i.subtotal, 0) / inv.subtotalAmount : 1;
    return Math.round(inv.totalAmount * ratio * 100) / 100;
  });

  constructor() {
    this.permissions.refresh().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => {} });
    this.loadHistory();
  }

  loadHistory(): void {
    this.loading.set(true);
    this.errorMessage.set('');
    this.posService
      .listInvoices(
        this.searchQuery(),
        this.selectedPayment(),
        this.filterTodayOnly() ? '2026-11-13' : '',
      )
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: res => {
          this.invoices.set(res.invoices);
          this.totalCount.set(res.totalCount || res.invoices.length);
          const curId = this.selectedInvoice()?.invoiceId;
          const match = res.invoices.find(i => i.invoiceId === curId) ?? res.invoices[0] ?? null;
          this.selectedInvoice.set(match);
        },
        error: () => {
          this.errorMessage.set('Unable to load counter invoice history.');
        },
      });
  }

  onSearch(val: string): void {
    this.searchQuery.set(val);
    this.currentPage.set(1);
    this.loadHistory();
  }

  selectPaymentFilter(mode: string): void {
    this.selectedPayment.set(mode);
    this.currentPage.set(1);
    this.loadHistory();
  }

  toggleDateFilter(): void {
    this.filterTodayOnly.set(!this.filterTodayOnly());
    this.currentPage.set(1);
    this.loadHistory();
  }

  selectRow(inv: PosInvoice): void {
    this.selectedInvoice.set(inv);
    this.errorMessage.set('');
    this.feedbackMessage.set('');
  }

  openMultiStepVoid(): void {
    const inv = this.selectedInvoice();
    if (!inv) return;
    if (!this.isOwner()) {
      this.errorMessage.set('Owner authorization is required to initiate invoice voids or refunds.');
      return;
    }
    const firstActive = inv.items.filter(i => !i.isRefunded).map(i => i.itemId);
    this.selectedVoidItemIds.set(firstActive.slice(0, 1));
    this.errorMessage.set('');
    this.showMultiStepVoidModal.set(true);
  }

  toggleVoidItemSelection(itemId: number): void {
    this.selectedVoidItemIds.update(ids =>
      ids.includes(itemId) ? ids.filter(x => x !== itemId) : [...ids, itemId],
    );
  }

  confirmInlineVoid(): void {
    const inv = this.selectedInvoice();
    if (!inv || this.submitting()) return;
    if (!this.isOwner()) {
      this.errorMessage.set('Only an Owner account can confirm an invoice void.');
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');
    this.posService
      .voidOrRefundInvoice(inv.invoiceId, {
        reason: this.inlineReason(),
        ownerPin: this.inlinePin(),
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.submitting.set(false)),
      )
      .subscribe({
        next: updated => {
          this.selectedInvoice.set(updated);
          this.feedbackMessage.set(
            `Invoice #${updated.invoiceNumber} marked ${updated.invoiceStatus} and showroom stock restored.`,
          );
          this.loadHistory();
        },
        error: err => {
          this.errorMessage.set(err?.error?.message || 'Unable to void invoice.');
        },
      });
  }

  confirmMultiStepVoid(): void {
    const inv = this.selectedInvoice();
    if (!inv || this.submitting()) return;
    if (this.selectedVoidItemIds().length === 0) {
      this.errorMessage.set('Select at least one line item to void or refund.');
      return;
    }

    this.submitting.set(true);
    this.errorMessage.set('');
    this.posService
      .voidOrRefundInvoice(inv.invoiceId, {
        reason: this.modalVoidReason(),
        ownerPin: this.modalOwnerPin(),
        itemIds: this.selectedVoidItemIds(),
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.submitting.set(false)),
      )
      .subscribe({
        next: updated => {
          this.selectedInvoice.set(updated);
          this.showMultiStepVoidModal.set(false);
          this.feedbackMessage.set(
            `Invoice #${updated.invoiceNumber} updated (${updated.invoiceStatus}) and audit_logs recorded.`,
          );
          this.loadHistory();
        },
        error: err => {
          this.errorMessage.set(err?.error?.message || 'Void / refund request failed.');
        },
      });
  }

  printReceipt(): void {
    window.print();
  }

  statusBadgeClass(status: string): string {
    switch (status) {
      case 'PENDING':
        return 'st-pending';
      case 'VOIDED':
        return 'st-voided';
      case 'REFUNDED':
        return 'st-refunded';
      default:
        return 'st-paid';
    }
  }

  statusLabel(status: string): string {
    switch (status) {
      case 'PENDING':
        return 'Pending';
      case 'VOIDED':
        return 'Voided';
      case 'REFUNDED':
        return 'Refunded';
      default:
        return 'Paid';
    }
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
}
