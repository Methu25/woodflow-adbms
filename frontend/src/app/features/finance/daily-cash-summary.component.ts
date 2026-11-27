import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import {
  DailyCashCategorySummaryRow,
  DailyCashMovementBar,
  DailyCashSummaryResponse,
  EmployeeAdvanceService,
  FALLBACK_DAILY_CASH_SUMMARY,
} from './employee-advance.service';

@Component({
  selector: 'app-daily-cash-summary',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, RouterLinkActive],
  templateUrl: './daily-cash-summary.component.html',
  styleUrls: [
    './finance-header.scss',
    './daily-cash-summary.component.scss',
    './daily-cash-table.scss',
  ],
})
export class DailyCashSummaryComponent {
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

  readonly availableDates = [
    'Fri, 13 Nov 2026',
    'Thu, 12 Nov 2026',
    'Wed, 11 Nov 2026',
    'Tue, 10 Nov 2026',
  ];

  readonly selectedDate = signal<string>('Fri, 13 Nov 2026');
  readonly searchTerm = signal<string>('');
  readonly loading = signal<boolean>(false);
  readonly error = signal<string>('');
  readonly success = signal<string>('');

  readonly summaryData = signal<DailyCashSummaryResponse>(FALLBACK_DAILY_CASH_SUMMARY);

  readonly filteredCategoryRows = computed<DailyCashCategorySummaryRow[]>(() => {
    const rows = this.summaryData().categoryRows;
    const term = this.searchTerm().trim().toLowerCase();
    if (!term) return rows;
    return rows.filter(r => r.categoryName.toLowerCase().includes(term));
  });

  readonly incomingBars = computed<DailyCashMovementBar[]>(() => this.summaryData().incomingBars);
  readonly outgoingBars = computed<DailyCashMovementBar[]>(() => this.summaryData().outgoingBars);

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
    this.loadDailySummary();
  }

  loadDailySummary(): void {
    this.loading.set(true);
    this.error.set('');
    this.service
      .getDailyCashSummary(this.selectedDate())
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: res => {
          this.summaryData.set(res);
        },
        error: () => {
          this.error.set('Could not load the Daily Cash Summary.');
        },
      });
  }

  cycleDate(): void {
    const idx = this.availableDates.indexOf(this.selectedDate());
    const nextIdx = (idx + 1) % this.availableDates.length;
    this.selectedDate.set(this.availableDates[nextIdx]);
    this.loadDailySummary();
  }

  printSummary(): void {
    this.success.set(
      `Prepared EOD Daily Cash Summary sheet (${this.summaryData().dateLabel} · Closing Balance ${this.formatLkr(this.summaryData().closingBalanceAmount)}) for printing.`,
    );
    if (typeof window !== 'undefined' && typeof window.print === 'function') {
      window.print();
    }
  }

  formatLkr(amount: number): string {
    return `Rs. ${this.formatNumber(Math.abs(amount))}`;
  }

  formatSignedLkr(amount: number): string {
    const sign = amount >= 0 ? '+' : '-';
    return `${sign} Rs. ${this.formatNumber(Math.abs(amount))}`;
  }

  formatTableCell(amount: number | null, prefix: '+' | '-'): string {
    if (amount === null || amount === undefined) return '—';
    return `${prefix} ${this.formatNumber(Math.abs(amount))}`;
  }

  formatNetCell(amount: number): string {
    const sign = amount >= 0 ? '+' : '-';
    return `${sign} ${this.formatNumber(Math.abs(amount))}`;
  }

  formatWholeLkr(amount: number): string {
    return `Rs. ${Math.round(Math.abs(amount)).toLocaleString('en-US')}`;
  }

  formatNumber(amount: number): string {
    return amount.toLocaleString('en-US', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    });
  }
}
