import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import {
  EmployeeAdvanceLedgerRow,
  EmployeeAdvanceService,
} from './employee-advance.service';

export type StaffLedgerFilterTab = 'ALL' | 'OUTSTANDING' | 'SETTLED';

@Component({
  selector: 'app-advance-ledger',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, RouterLinkActive],
  templateUrl: './advance-ledger.component.html',
  styleUrls: [
    './finance-header.scss',
    './advance-ledger.component.scss',
    './advance-ledger-drawer.scss',
  ],
})
export class AdvanceLedgerComponent {
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
  readonly settling = signal<boolean>(false);
  readonly error = signal<string>('');
  readonly success = signal<string>('');

  readonly activeFilter = signal<StaffLedgerFilterTab>('ALL');
  readonly searchTerm = signal<string>('');

  readonly totalAdvancesIssuedMonth = signal<number>(248500);
  readonly settledViaPayrollOrCash = signal<number>(196000);
  readonly activeOutstandingBalance = signal<number>(52500);
  readonly activeOutstandingStaffCount = signal<number>(4);
  readonly avgRecoveryPeriodLabel = signal<string>('1.4 Months');

  readonly allStaffCount = signal<number>(12);
  readonly outstandingStaffCount = signal<number>(4);
  readonly settledStaffCount = signal<number>(8);

  readonly staffRows = signal<EmployeeAdvanceLedgerRow[]>([]);
  readonly selectedEmployeeId = signal<number>(14);

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

  readonly selectedEmployeeRow = computed<EmployeeAdvanceLedgerRow>(() => {
    const rows = this.staffRows();
    const id = this.selectedEmployeeId();
    return (
      rows.find(r => r.employeeId === id) ??
      rows[0] ?? {
        employeeId: 14,
        employeeCode: 'EMP-014',
        fullName: 'Kasun Perera',
        roleTitle: 'Carpenter',
        department: 'Carpentry',
        totalAdvancesAmount: 78500,
        settledRecoveriesAmount: 70000,
        outstandingBalance: 8500,
        lastTransactionDateLabel: '13 Nov',
        status: 'DEFICIT',
        statusLabel: 'Deficit',
        recoveryProgressPercent: 89,
        transactions: [
          {
            transactionId: 'ADV-1113',
            title: 'Cash Advance #ADV-1113',
            dateLabel: '13 Nov 2026',
            flowType: 'OUT',
            amount: 25000,
          },
          {
            transactionId: 'ADV-1104',
            title: 'Mid-Month Cash Draw #2',
            dateLabel: '04 Nov 2026',
            flowType: 'OUT',
            amount: 28500,
          },
          {
            transactionId: 'REC-1031',
            title: 'Oct Payroll Recovery',
            dateLabel: '31 Oct 2026',
            flowType: 'IN',
            amount: 45000,
          },
          {
            transactionId: 'ADV-1022',
            title: 'Emergency Advance #1',
            dateLabel: '22 Oct 2026',
            flowType: 'OUT',
            amount: 25000,
          },
        ],
      }
    );
  });

  constructor() {
    this.loadLedger();
  }

  loadLedger(): void {
    this.loading.set(true);
    this.error.set('');
    this.service
      .getAdvanceLedger(this.activeFilter(), this.searchTerm())
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: res => {
          this.totalAdvancesIssuedMonth.set(res.totalAdvancesIssuedMonth);
          this.settledViaPayrollOrCash.set(res.settledViaPayrollOrCash);
          this.activeOutstandingBalance.set(res.activeOutstandingBalance);
          this.activeOutstandingStaffCount.set(res.activeOutstandingStaffCount);
          this.avgRecoveryPeriodLabel.set(res.avgRecoveryPeriodLabel);
          this.allStaffCount.set(res.allStaffCount);
          this.outstandingStaffCount.set(res.outstandingStaffCount);
          this.settledStaffCount.set(res.settledStaffCount);
          this.staffRows.set(res.staffRows);

          if (res.staffRows.length > 0 && !res.staffRows.some(r => r.employeeId === this.selectedEmployeeId())) {
            this.selectedEmployeeId.set(res.staffRows[0].employeeId);
          }
        },
        error: () => {
          this.error.set('Could not load the Employee Advance Ledger.');
        },
      });
  }

  setFilter(tab: StaffLedgerFilterTab): void {
    if (this.activeFilter() === tab) return;
    this.activeFilter.set(tab);
    this.loadLedger();
  }

  onSearchChange(value: string): void {
    this.searchTerm.set(value);
    this.loadLedger();
  }

  selectRow(row: EmployeeAdvanceLedgerRow): void {
    this.selectedEmployeeId.set(row.employeeId);
  }

  settleRow(row: EmployeeAdvanceLedgerRow, event?: Event): void {
    if (event) {
      event.stopPropagation();
    }
    this.selectedEmployeeId.set(row.employeeId);
    if (row.outstandingBalance <= 0 || this.settling()) return;

    this.settling.set(true);
    this.error.set('');
    this.success.set('');

    this.service
      .settleEmployeeAdvance({
        employeeId: row.employeeId,
        settlementAmount: row.outstandingBalance,
        settlementMethod: 'PAYROLL_DEDUCT',
        settlementPeriod: '2026-11',
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.settling.set(false)),
      )
      .subscribe({
        next: res => {
          this.success.set(res.message);
          this.loadLedger();
        },
        error: (err: unknown) => {
          if (err instanceof HttpErrorResponse && err.error?.message) {
            this.error.set(err.error.message);
          } else {
            this.error.set('Unable to settle employee advance balance.');
          }
        },
      });
  }

  printStatement(): void {
    const emp = this.selectedEmployeeRow();
    this.success.set(
      `Prepared Employee Advance Statement for ${emp.fullName} (${emp.employeeCode}). Opening print dialog…`,
    );
    if (typeof window !== 'undefined' && typeof window.print === 'function') {
      window.print();
    }
  }

  formatLkr(amount: number): string {
    return (
      'Rs. ' +
      Math.abs(amount).toLocaleString('en-US', {
        minimumFractionDigits: 2,
        maximumFractionDigits: 2,
      })
    );
  }

  formatWholeLkr(amount: number): string {
    return (
      'Rs. ' +
      Math.round(Math.abs(amount)).toLocaleString('en-US', {
        maximumFractionDigits: 0,
      })
    );
  }
}
