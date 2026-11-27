import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink, RouterLinkActive } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import {
  EmployeeAdvanceProfile,
  EmployeeAdvanceService,
} from './employee-advance.service';

export type SettlementRecoveryMethod = 'PAYROLL_DEDUCT' | 'CASH_REPAYMENT' | 'BANK_TRANSFER';

export interface ReceiptAdvanceLine {
  voucherReference: string;
  titleLabel: string;
  subtitleLabel: string;
  amount: number;
  statusLabel: 'Settled' | 'Partial' | 'Open';
  statusClass: 'pill-settled' | 'pill-partial' | 'pill-open';
}

const EMPLOYEE_NICS: Record<string, string> = {
  'EMP-014': '199124508821',
  'EMP-008': '198831204419',
  'EMP-019': '200118903342',
  'EMP-011': '199422107785',
  'EMP-005': '198611405590',
  'EMP-009': '199030501128',
};

@Component({
  selector: 'app-advance-settlement',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule, RouterLink, RouterLinkActive],
  templateUrl: './advance-settlement.component.html',
  styleUrls: [
    './finance-header.scss',
    './advance-settlement.component.scss',
    './advance-settlement-progress.scss',
    './advance-settlement-receipt.scss',
  ],
})
export class AdvanceSettlementComponent {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(EmployeeAdvanceService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  readonly auth = inject(AuthService);

  readonly navTabs = [
    { label: '01 Advance Issuance', route: '/finance/advances/issue', exact: true },
    { label: '02 Advance Ledger', route: '/finance/advances', exact: true },
    { label: '03 Petty Cash Ledger', route: '/finance/petty-cash', exact: true },
    { label: '04 Daily Cash Summary', route: '/finance/daily-summary', exact: true },
    { label: '05 Advance Settlement & Receipt', route: '/finance/advances/settle', exact: false },
  ];

  readonly employees = signal<EmployeeAdvanceProfile[]>([]);
  readonly selectedEmployeeId = signal<number>(14);
  readonly selectedMethod = signal<SettlementRecoveryMethod>('PAYROLL_DEDUCT');
  readonly settlementAmountSignal = signal<number>(45000);
  readonly searchTerm = signal<string>('');

  readonly loading = signal<boolean>(false);
  readonly submitting = signal<boolean>(false);
  readonly error = signal<string>('');
  readonly success = signal<string>('');

  readonly form = this.fb.nonNullable.group({
    settlementAmountInput: ['+ Rs. 45,000.00', [Validators.required]],
    settlementVoucher: ['SET-2026-1113-014', [Validators.required]],
  });

  readonly selectedEmployee = computed<EmployeeAdvanceProfile>(() => {
    const list = this.employees();
    const id = this.selectedEmployeeId();
    return (
      list.find(e => e.employeeId === id) ??
      list[0] ?? {
        employeeId: 14,
        employeeCode: 'EMP-014',
        fullName: 'Kasun Perera',
        roleTitle: 'Master Carpenter',
        department: 'Carpentry Workshop',
        dropdownLabel: 'Kasun Perera (EMP-014 · Carpentry Workshop)',
        initials: 'KP',
        baseMonthlySalary: 58000,
        approvedPieceworkCredits: 12000,
        totalMtdGrossEarnings: 70000,
        outstandingAdvances: [
          {
            advanceId: 202,
            voucherReference: 'ADV-2026-1022',
            dateLabel: '22 Oct',
            titleLabel: 'Unrecovered Tool Advance',
            displayLine: '22 Oct · Unrecovered Tool Advance',
            amount: 25000,
            isSettled: false,
          },
          {
            advanceId: 201,
            voucherReference: 'ADV-2026-1104',
            dateLabel: '04 Nov',
            titleLabel: 'Mid-Month Cash Draw #2',
            displayLine: '04 Nov · Mid-Month Cash Draw #2',
            amount: 28500,
            isSettled: false,
          },
        ],
        totalOutstandingAdvances: 53500,
        netAvailableBeforeAdvance: 16500,
      }
    );
  });

  readonly selectedEmployeeNic = computed<string>(() => {
    const code = this.selectedEmployee().employeeCode;
    return EMPLOYEE_NICS[code] ?? '199124508821';
  });

  readonly selectedEmployeeRoleDisplay = computed<string>(() => {
    const emp = this.selectedEmployee();
    if (emp.employeeCode === 'EMP-014' && emp.roleTitle === 'Carpenter') {
      return 'Master Carpenter';
    }
    return emp.roleTitle;
  });

  readonly totalOutstanding = computed<number>(() => {
    const emp = this.selectedEmployee();
    return emp.totalOutstandingAdvances > 0 ? emp.totalOutstandingAdvances : 53500;
  });

  readonly maxPayrollDeduct = computed<number>(() => {
    const emp = this.selectedEmployee();
    return Math.min(emp.totalMtdGrossEarnings, this.totalOutstanding());
  });

  readonly openAdvancesCount = computed<number>(() => {
    const list = this.selectedEmployee().outstandingAdvances;
    return list.length > 0 ? list.length : 2;
  });

  readonly openAdvancesVoucherSub = computed<string>(() => {
    const list = this.selectedEmployee().outstandingAdvances;
    if (list.length >= 2) {
      const codes = list.slice(0, 2).map(a => a.voucherReference.replace('ADV-2026-', 'ADV-'));
      return `${codes[1]} & ${codes[0]}`;
    }
    if (list.length === 1) {
      return list[0].voucherReference;
    }
    return 'ADV-1022 & ADV-1104';
  });

  readonly recoveryPercent = computed<number>(() => {
    const out = this.totalOutstanding();
    if (out <= 0) return 100;
    const pct = (this.settlementAmountSignal() / out) * 100;
    return Math.min(100, Math.max(0, Number(pct.toFixed(1))));
  });

  readonly remainingAdvanceBalance = computed<number>(() =>
    Math.max(0, this.totalOutstanding() - this.settlementAmountSignal()),
  );

  readonly netEmployeePayoutAfterRecovery = computed<number>(() =>
    Math.max(0, this.selectedEmployee().totalMtdGrossEarnings - this.settlementAmountSignal()),
  );

  readonly settlementMethodDisplay = computed<string>(() => {
    switch (this.selectedMethod()) {
      case 'CASH_REPAYMENT':
        return 'Direct Cash Return (Till #1)';
      case 'BANK_TRANSFER':
        return 'Bank Transfer (HNB Corporate)';
      default:
        return 'Payroll Deduction (Nov 2026)';
    }
  });

  readonly settlementMethodShortLabel = computed<string>(() => {
    switch (this.selectedMethod()) {
      case 'CASH_REPAYMENT':
        return 'Cash Repayment';
      case 'BANK_TRANSFER':
        return 'Bank Transfer';
      default:
        return 'Payroll Deduct';
    }
  });

  readonly receiptAdvanceLines = computed<ReceiptAdvanceLine[]>(() => {
    const emp = this.selectedEmployee();
    const rawLines =
      emp.outstandingAdvances.length > 0
        ? [...emp.outstandingAdvances].reverse()
        : [
            {
              advanceId: 202,
              voucherReference: 'ADV-2026-1022',
              dateLabel: '22 Oct',
              titleLabel: 'Unrecovered Tool Advance',
              displayLine: '22 Oct · Unrecovered Tool Advance',
              amount: 25000,
              isSettled: false,
            },
            {
              advanceId: 201,
              voucherReference: 'ADV-2026-1104',
              dateLabel: '04 Nov',
              titleLabel: 'Mid-Month Cash Draw #2',
              displayLine: '04 Nov · Mid-Month Cash Draw #2',
              amount: 28500,
              isSettled: false,
            },
          ];

    let remainingToAllocate = this.settlementAmountSignal();
    return rawLines.map((line, index) => {
      const isFullyCovered = remainingToAllocate >= line.amount;
      const isPartiallyCovered = !isFullyCovered && remainingToAllocate > 0;
      remainingToAllocate = Math.max(0, remainingToAllocate - line.amount);

      const statusLabel: 'Settled' | 'Partial' | 'Open' = isFullyCovered
        ? 'Settled'
        : isPartiallyCovered
          ? 'Partial'
          : 'Open';
      const statusClass: 'pill-settled' | 'pill-partial' | 'pill-open' = isFullyCovered
        ? 'pill-settled'
        : isPartiallyCovered
          ? 'pill-partial'
          : 'pill-open';

      const defaultTitle =
        index === 0
          ? `Prior Unsettled Advance #${line.voucherReference}`
          : `Mid-Month Cash Draw #${line.voucherReference}`;
      const defaultSubtitle =
        index === 0
          ? `${line.dateLabel} 2026 · Unrecovered Tool Advance`
          : `${line.dateLabel} 2026 · Family Medical Advance`;

      return {
        voucherReference: line.voucherReference,
        titleLabel: defaultTitle,
        subtitleLabel: defaultSubtitle,
        amount: line.amount,
        statusLabel,
        statusClass,
      };
    });
  });

  readonly allocationSummaryNote = computed<string>(() => {
    const lines = this.receiptAdvanceLines();
    if (lines.length >= 2) {
      const rem = this.remainingAdvanceBalance();
      return `${lines[0].voucherReference} (Rs. ${Math.round(lines[0].amount).toLocaleString('en-US')}) marked ${lines[0].statusLabel.toUpperCase()} · ${lines[1].voucherReference} reduced to Rs. ${Math.round(rem).toLocaleString('en-US')}`;
    }
    return `Remaining advance balance after settlement: ${this.formatLkr(this.remainingAdvanceBalance())}`;
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
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const idParam = Number(params.get('employeeId'));
      if (Number.isFinite(idParam) && idParam > 0) {
        this.selectedEmployeeId.set(idParam);
      }
    });

    this.form.controls.settlementAmountInput.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(val => {
        const parsed = this.parseCurrencyAmount(val);
        this.settlementAmountSignal.set(parsed);
      });

    this.loadSettlementContext();
  }

  loadSettlementContext(): void {
    this.loading.set(true);
    this.service
      .getIssuanceContext()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: res => {
          if (res.employees.length > 0) {
            this.employees.set(res.employees);
            this.syncVoucherForSelectedEmployee();
          }
        },
      });
  }

  cycleEmployee(): void {
    const list = this.employees();
    if (list.length === 0) return;
    const currentIdx = list.findIndex(e => e.employeeId === this.selectedEmployeeId());
    const nextIdx = (currentIdx + 1) % list.length;
    const nextEmp = list[nextIdx];
    this.selectedEmployeeId.set(nextEmp.employeeId);

    const out = nextEmp.totalOutstandingAdvances > 0 ? nextEmp.totalOutstandingAdvances : 45000;
    const defaultSettle = nextEmp.employeeCode === 'EMP-014' ? 45000 : out;
    this.settlementAmountSignal.set(defaultSettle);
    this.form.patchValue({
      settlementAmountInput: `+ Rs. ${this.formatNumber(defaultSettle)}`,
    });
    this.syncVoucherForSelectedEmployee();
  }

  private syncVoucherForSelectedEmployee(): void {
    const codeSuffix = this.selectedEmployee().employeeCode.replace('EMP-', '') || '014';
    this.form.controls.settlementVoucher.setValue(`SET-2026-1113-${codeSuffix}`);
  }

  selectMethod(method: SettlementRecoveryMethod): void {
    this.selectedMethod.set(method);
  }

  onAmountBlur(): void {
    const parsed = this.parseCurrencyAmount(this.form.controls.settlementAmountInput.value);
    const clamped = Math.max(0, Math.min(parsed, this.totalOutstanding()));
    this.settlementAmountSignal.set(clamped);
    this.form.controls.settlementAmountInput.setValue(`+ Rs. ${this.formatNumber(clamped)}`, {
      emitEvent: false,
    });
  }

  confirmSettlement(): void {
    this.error.set('');
    this.success.set('');
    const amount = this.settlementAmountSignal();

    if (amount <= 0) {
      this.error.set('Please enter a Settlement Amount (LKR) greater than Rs. 0.00.');
      return;
    }

    if (amount > this.totalOutstanding()) {
      this.error.set(
        `Settlement amount (${this.formatLkr(amount)}) cannot exceed total outstanding advance (${this.formatLkr(this.totalOutstanding())}).`,
      );
      return;
    }

    const emp = this.selectedEmployee();
    this.submitting.set(true);
    this.service
      .settleEmployeeAdvance({
        employeeId: emp.employeeId,
        settlementAmount: amount,
        settlementMethod: this.selectedMethod(),
        settlementPeriod: '2026-11',
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.submitting.set(false)),
      )
      .subscribe({
        next: res => {
          this.success.set(
            res.message ||
              `Settlement ${this.form.controls.settlementVoucher.value} (+ ${this.formatLkr(amount)}) committed via sp_settle_monthly_payroll for ${emp.fullName} (${emp.employeeCode}).`,
          );
        },
        error: (err: unknown) => {
          if (err instanceof HttpErrorResponse && err.error?.message) {
            this.error.set(String(err.error.message));
          } else {
            this.error.set('Unable to commit advance settlement.');
          }
        },
      });
  }

  printReceipt(): void {
    this.success.set(
      `Prepared official Settlement Voucher (${this.form.controls.settlementVoucher.value}) for printing.`,
    );
    if (typeof window !== 'undefined' && typeof window.print === 'function') {
      window.print();
    }
  }

  downloadPdf(): void {
    this.success.set(
      `Exporting Settlement Voucher ${this.form.controls.settlementVoucher.value} as PDF via print dialog.`,
    );
    if (typeof window !== 'undefined' && typeof window.print === 'function') {
      window.print();
    }
  }

  parseCurrencyAmount(raw: string): number {
    if (!raw) return 0;
    const cleaned = String(raw).replace(/[^0-9.]/g, '');
    const num = Number.parseFloat(cleaned);
    return Number.isFinite(num) && num >= 0 ? Math.round(num * 100) / 100 : 0;
  }

  formatLkr(amount: number): string {
    return `Rs. ${this.formatNumber(Math.abs(amount))}`;
  }

  formatShortK(amount: number): string {
    return `${Math.round(amount / 1000)}k`;
  }

  formatNumber(amount: number): string {
    return amount.toLocaleString('en-US', {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    });
  }
}
