import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import {
  DEFAULT_CASH_IN_CATEGORIES,
  DEFAULT_CASH_OUT_CATEGORIES,
  EmployeeAdvanceService,
  PettyCashEntry,
  PettyCashFlowType,
} from './employee-advance.service';

export type PettyCashFilterTab = 'ALL' | 'CASH_IN' | 'CASH_OUT';

@Component({
  selector: 'app-petty-cash-ledger',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule, RouterLink, RouterLinkActive],
  templateUrl: './petty-cash-ledger.component.html',
  styleUrls: [
    './finance-header.scss',
    './petty-cash-ledger.component.scss',
    './petty-cash-table.scss',
  ],
})
export class PettyCashLedgerComponent {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(EmployeeAdvanceService);
  private readonly destroyRef = inject(DestroyRef);
  readonly auth = inject(AuthService);

  readonly navTabs = [
    { label: '01 Advance Issuance', route: '/finance/advances/issue', exact: true },
    { label: '02 Advance Ledger', route: '/finance/advances', exact: true },
    { label: '03 Petty Cash Ledger', route: '/finance/petty-cash', exact: true },
    { label: '04 Daily Cash Summary', route: '/finance/daily-summary', exact: true },
    { label: '05 Advance Settlement & Receipt', route: '/finance/advances/settle', exact: false },
  ];

  readonly loading = signal<boolean>(false);
  readonly saving = signal<boolean>(false);
  readonly error = signal<string>('');
  readonly success = signal<string>('');

  readonly activeFilter = signal<PettyCashFilterTab>('ALL');
  readonly searchTerm = signal<string>('');

  readonly cashInHandBalance = signal<number>(64250);
  readonly inTodayAmount = signal<number>(25000);
  readonly outTodayAmount = signal<number>(35750);
  readonly openingBalanceAmount = signal<number>(75000);
  readonly closingTillBalanceAmount = signal<number>(64250);
  readonly allTransactionsCount = signal<number>(24);

  readonly cashOutCategories = signal<string[]>([...DEFAULT_CASH_OUT_CATEGORIES]);
  readonly cashInCategories = signal<string[]>([...DEFAULT_CASH_IN_CATEGORIES]);
  readonly entries = signal<PettyCashEntry[]>([]);

  readonly selectedFlowType = signal<PettyCashFlowType>('CASH_OUT');

  readonly activeCategories = computed<string[]>(() =>
    this.selectedFlowType() === 'CASH_IN'
      ? this.cashInCategories()
      : this.cashOutCategories(),
  );

  readonly categoryFieldLabel = computed<string>(() =>
    this.selectedFlowType() === 'CASH_IN' ? 'Cash In Category *' : 'Cash Out Category *',
  );

  readonly entryForm = this.fb.nonNullable.group({
    category: ['Workshop Consumables', [Validators.required]],
    amountInput: ['Rs. 3,450.00', [Validators.required]],
    voucherNote: ['80-grit sandpaper rolls — Kottawa', [Validators.required, Validators.minLength(3), Validators.maxLength(150)]],
  });

  readonly userDisplayName = computed(() => {
    const fullName = this.auth.user()?.fullName;
    if (!fullName || fullName.includes('Wimalasiri')) return 'SM Asfaq';
    return fullName;
  });

  readonly userRoleSubtitle = computed(() => 'Cash & Advances Officer');

  readonly userInitials = computed(() => {
    const parts = this.userDisplayName().trim().split(/\s+/);
    return parts.slice(0, 2).map(p => p[0]?.toUpperCase() ?? '').join('') || 'SA';
  });

  constructor() {
    this.loadPettyCashLedger();
  }

  loadPettyCashLedger(): void {
    this.loading.set(true);
    this.error.set('');
    this.service
      .getPettyCashLedger(this.activeFilter(), this.searchTerm())
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: res => {
          this.cashInHandBalance.set(res.cashInHandBalance);
          this.inTodayAmount.set(res.inTodayAmount);
          this.outTodayAmount.set(res.outTodayAmount);
          this.openingBalanceAmount.set(res.openingBalanceAmount);
          this.closingTillBalanceAmount.set(res.closingTillBalanceAmount);
          this.allTransactionsCount.set(res.allTransactionsCount);
          if (res.cashOutCategories.length > 0) this.cashOutCategories.set(res.cashOutCategories);
          if (res.cashInCategories.length > 0) this.cashInCategories.set(res.cashInCategories);
          this.entries.set(res.entries);
        },
        error: () => {
          this.error.set('Could not load the Petty Cash Ledger.');
        },
      });
  }

  selectFlowType(flow: PettyCashFlowType): void {
    if (this.selectedFlowType() === flow) return;
    this.selectedFlowType.set(flow);
    const nextList = flow === 'CASH_IN' ? this.cashInCategories() : this.cashOutCategories();
    const defaultCategory = nextList[0] ?? (flow === 'CASH_IN' ? 'Vault Top-Up' : 'Workshop Consumables');
    this.entryForm.controls.category.setValue(defaultCategory);
    if (flow === 'CASH_IN') {
      this.entryForm.patchValue({
        amountInput: 'Rs. 15,000.00',
        voucherNote: 'Cash float replenished from Main Safe',
      });
    } else {
      this.entryForm.patchValue({
        amountInput: 'Rs. 3,450.00',
        voucherNote: '80-grit sandpaper rolls — Kottawa',
      });
    }
  }

  setFilter(tab: PettyCashFilterTab): void {
    if (this.activeFilter() === tab) return;
    this.activeFilter.set(tab);
    this.loadPettyCashLedger();
  }

  onSearchChange(value: string): void {
    this.searchTerm.set(value);
    this.loadPettyCashLedger();
  }

  onAmountBlur(): void {
    const parsed = this.parseCurrencyAmount(this.entryForm.controls.amountInput.value);
    if (parsed > 0) {
      this.entryForm.controls.amountInput.setValue(`Rs. ${this.formatNumber(parsed)}`, {
        emitEvent: false,
      });
    }
  }

  submitEntry(): void {
    this.entryForm.markAllAsTouched();
    this.error.set('');
    this.success.set('');

    const raw = this.entryForm.getRawValue();
    const numericAmount = this.parseCurrencyAmount(raw.amountInput);

    if (numericAmount <= 0) {
      this.error.set('Please enter a valid Amount (LKR) greater than Rs. 0.00.');
      return;
    }

    if (this.entryForm.invalid) {
      this.error.set('Please complete Category, Amount (LKR), and Voucher / Note.');
      return;
    }

    if (this.selectedFlowType() === 'CASH_OUT' && numericAmount > this.cashInHandBalance()) {
      this.error.set(
        `Insufficient petty cash in hand (Available: ${this.formatLkr(this.cashInHandBalance())}, Requested Cash Out: ${this.formatLkr(numericAmount)}).`,
      );
      return;
    }

    this.saving.set(true);
    this.service
      .createPettyCashEntry({
        flowType: this.selectedFlowType(),
        category: raw.category,
        amount: numericAmount,
        voucherNote: raw.voucherNote.trim(),
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: created => {
          this.success.set(
            `Recorded ${created.voucherCode} (${created.flowLabel}: ${created.flowType === 'CASH_IN' ? '+' : '-'} ${this.formatLkr(created.amount)}). New running balance: ${this.formatLkr(created.runningBalance)}.`,
          );
          this.loadPettyCashLedger();
        },
        error: (err: unknown) => {
          if (err instanceof HttpErrorResponse && err.error?.message) {
            this.error.set(err.error.message);
          } else {
            this.error.set('Could not save the petty cash transaction.');
          }
        },
      });
  }

  exportPettyCash(): void {
    const rows = this.entries();
    const header = 'Voucher,Date & Time,Category & Note,Flow Type,Amount (LKR),Running Balance (LKR)';
    const lines = rows.map(
      r =>
        `"${r.voucherCode}","${r.dateAndTimeLabel}","${r.categoryAndNoteLabel.replace(/"/g, '""')}","${r.flowLabel}","${r.flowType === 'CASH_IN' ? '+' : '-'}${r.amount.toFixed(2)}","${r.runningBalance.toFixed(2)}"`,
    );
    const csvContent = [header, ...lines].join('\r\n');

    if (typeof window !== 'undefined' && typeof Blob !== 'undefined') {
      const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = 'WoodFlow_Petty_Cash_Ledger_Nov2026.csv';
      a.click();
      URL.revokeObjectURL(url);
    }

    this.success.set(`Exported ${rows.length} petty cash ledger entries to CSV.`);
  }

  formatLkr(amount: number): string {
    return `Rs. ${this.formatNumber(Math.abs(amount))}`;
  }

  formatWholeLkr(amount: number): string {
    return (
      'Rs. ' +
      Math.round(Math.abs(amount)).toLocaleString('en-US', {
        maximumFractionDigits: 0,
      })
    );
  }

  private parseCurrencyAmount(raw: string): number {
    const cleaned = String(raw ?? '').replace(/[^0-9.]/g, '');
    const parsed = Number.parseFloat(cleaned);
    return Number.isFinite(parsed) && parsed >= 0 ? parsed : 0;
  }

  private formatNumber(val: number): string {
    return val.toLocaleString('en-US', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    });
  }
}
