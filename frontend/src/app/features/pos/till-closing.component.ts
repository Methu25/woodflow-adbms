import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { PermissionsService } from '../../core/auth/permissions.service';
import { PosCounterService, PosTillSummary } from './pos-counter.service';

@Component({
  selector: 'app-till-closing',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, RouterLinkActive],
  templateUrl: './till-closing.component.html',
  styleUrls: [
    './pos-header.scss',
    './pos-dialogs.scss',
    './till-closing.component.scss',
    './till-closing-locked.scss',
  ],
})
export class TillClosingComponent {
  private readonly posService = inject(PosCounterService);
  private readonly destroyRef = inject(DestroyRef);
  readonly auth = inject(AuthService);
  readonly permissions = inject(PermissionsService);

  readonly navTabs = [
    { label: '01 Counter POS / Billing', route: '/pos', exact: true },
    { label: '02 Invoice History & Reprint', route: '/pos/invoices', exact: false },
    { label: '03 Till Closing', route: '/pos/till-closing', exact: false },
  ];

  readonly summary = signal<PosTillSummary | null>(null);
  readonly notes5000 = signal<number>(26);
  readonly notes1000 = signal<number>(38);
  readonly notes500 = signal<number>(8);
  readonly notes100 = signal<number>(12);
  readonly coins50 = signal<number>(6);

  readonly loading = signal<boolean>(false);
  readonly submitting = signal<boolean>(false);
  readonly showCloseConfirmDialog = signal<boolean>(false);
  readonly showZReportDialog = signal<boolean>(false);
  readonly statusNotice = signal<string>('');
  readonly errorMessage = signal<string>('');

  readonly isOwner = computed(() => this.auth.user()?.role === 'OWNER');
  readonly cashierName = computed(() => this.auth.user()?.fullName || 'Yohan Methusael E J');
  readonly cashierRole = computed(() => (this.isOwner() ? 'Owner · Cashier' : 'Cashier'));

  readonly isClosed = computed(() => this.summary()?.sessionStatus === 'CLOSED');
  readonly openingFloat = computed(() => this.summary()?.openingFloat ?? 25000);
  readonly cashSales = computed(() => this.summary()?.cashSalesAmount ?? 148500);
  readonly cardSales = computed(() => this.summary()?.cardSalesAmount ?? 182000);
  readonly bankSlipSales = computed(() => this.summary()?.bankSlipAmount ?? 65000);
  readonly grossRevenue = computed(() => this.cashSales() + this.cardSales() + this.bankSlipSales());
  readonly expectedCash = computed(() => this.openingFloat() + this.cashSales());

  readonly countedCash = computed(
    () =>
      this.notes5000() * 5000 +
      this.notes1000() * 1000 +
      this.notes500() * 500 +
      this.notes100() * 100 +
      this.coins50() * 50,
  );
  readonly variance = computed(() => this.countedCash() - this.expectedCash());
  readonly safeDepositCash = computed(() => Math.max(0, this.countedCash() - this.openingFloat()));

  constructor() {
    this.permissions.refresh().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => {} });
    this.loadTill();
  }

  loadTill(): void {
    this.loading.set(true);
    this.posService
      .getTillSummary()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: res => {
          this.summary.set(res);
          this.notes5000.set(res.notes5000Count);
          this.notes1000.set(res.notes1000Count);
          this.notes500.set(res.notes500Count);
          this.notes100.set(res.notes100Count);
          this.coins50.set(res.coins50Count);
        },
        error: () => {
          this.errorMessage.set('Unable to load register till session.');
        },
      });
  }

  onDenomChange(): void {
    if (this.isClosed()) return;
    this.posService
      .updateTillCount({
        notes5000Count: Math.max(0, this.notes5000()),
        notes1000Count: Math.max(0, this.notes1000()),
        notes500Count: Math.max(0, this.notes500()),
        notes100Count: Math.max(0, this.notes100()),
        coins50Count: Math.max(0, this.coins50()),
      })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: res => this.summary.set(res), error: () => {} });
  }

  confirmCloseDay(): void {
    if (this.submitting() || this.isClosed()) return;
    this.submitting.set(true);
    this.errorMessage.set('');

    this.posService
      .closeTillDay({
        notes5000Count: Math.max(0, this.notes5000()),
        notes1000Count: Math.max(0, this.notes1000()),
        notes500Count: Math.max(0, this.notes500()),
        notes100Count: Math.max(0, this.notes100()),
        coins50Count: Math.max(0, this.coins50()),
        verifiedByName: 'Wimalasiri (Owner)',
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.submitting.set(false)),
      )
      .subscribe({
        next: closed => {
          this.summary.set(closed);
          this.showCloseConfirmDialog.set(false);
          this.showZReportDialog.set(true);
          this.statusNotice.set(
            `Day Locked · Register ${closed.tillNumber} Closed · Z-Report #${closed.zReportNumber} finalized.`,
          );
        },
        error: err => {
          this.errorMessage.set(err?.error?.message || 'Unable to close till day.');
        },
      });
  }

  printReport(): void {
    window.print();
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
}
