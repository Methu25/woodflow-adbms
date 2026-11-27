import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import {
  EmployeeAdvanceProfile,
  EmployeeAdvanceService,
  IssueEmployeeAdvanceRequest,
} from './employee-advance.service';

@Component({
  selector: 'app-advance-issuance',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './advance-issuance.component.html',
  styleUrls: [
    './finance-header.scss',
    './advance-issuance.component.scss',
    './advance-issuance-alert.scss',
    './advance-issuance-impact.scss',
  ],
})
export class AdvanceIssuanceComponent {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(EmployeeAdvanceService);
  private readonly destroyRef = inject(DestroyRef);
  readonly auth = inject(AuthService);

  readonly employees = signal<EmployeeAdvanceProfile[]>([]);
  readonly disbursementSources = signal<string[]>([
    'Cash Vault — Till #1 (Avail: Rs. 164,250)',
    'Petty Cash Vault — Till #2 (Avail: Rs. 64,250)',
    'Bank Transfer — HNB Corporate Account',
  ]);
  readonly selectedEmployeeId = signal<number>(14);
  readonly advanceAmountSignal = signal<number>(25000);
  readonly supervisorOverrideSignal = signal<boolean>(true);

  readonly loading = signal<boolean>(false);
  readonly submitting = signal<boolean>(false);
  readonly error = signal<string>('');
  readonly success = signal<string>('');

  readonly form = this.fb.nonNullable.group({
    employeeId: [14, [Validators.required]],
    advanceAmountInput: ['Rs. 25,000.00', [Validators.required]],
    issuanceDate: ['13 Nov 2026', [Validators.required]],
    disbursementSource: ['Cash Vault — Till #1 (Avail: Rs. 164,250)', [Validators.required]],
    voucherReference: ['ADV-2026-1113', [Validators.required, Validators.maxLength(30)]],
    reason: [
      'Urgent family hospitalisation bill settlement — employee requested mid-month draw.\nRecovery acknowledged against November & December payroll cycles.',
      [Validators.required, Validators.minLength(5), Validators.maxLength(500)],
    ],
    supervisorOverrideApproved: [true],
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

  readonly selectedEmployee = computed<EmployeeAdvanceProfile>(() => {
    const list = this.employees();
    const id = this.selectedEmployeeId();
    return (
      list.find(e => e.employeeId === id) ??
      list[0] ?? {
        employeeId: 14,
        employeeCode: 'EMP-014',
        fullName: 'Kasun Perera',
        roleTitle: 'Carpenter',
        department: 'Carpentry Workshop',
        dropdownLabel: 'Kasun Perera (EMP-014 · Carpentry Workshop)',
        initials: 'KP',
        baseMonthlySalary: 58000,
        approvedPieceworkCredits: 12000,
        totalMtdGrossEarnings: 70000,
        outstandingAdvances: [
          {
            advanceId: 201,
            voucherReference: 'ADV-2026-1104',
            dateLabel: '04 Nov',
            titleLabel: 'Mid-Month Draw #2',
            displayLine: '04 Nov · Mid-Month Draw #2',
            amount: 28500,
            isSettled: false,
          },
          {
            advanceId: 202,
            voucherReference: 'ADV-2026-1022',
            dateLabel: '22 Oct',
            titleLabel: 'Unrecovered Tool Advance',
            displayLine: '22 Oct · Unrecovered Tool Advance',
            amount: 25000,
            isSettled: false,
          },
        ],
        totalOutstandingAdvances: 53500,
        netAvailableBeforeAdvance: 16500,
      }
    );
  });

  readonly netAvailableBefore = computed(() => this.selectedEmployee().netAvailableBeforeAdvance);

  readonly balanceAfterNewAdvance = computed(
    () => this.netAvailableBefore() - this.advanceAmountSignal(),
  );

  readonly isDeficit = computed(() => this.balanceAfterNewAdvance() < 0);

  readonly deficitAbsoluteAmount = computed(() => Math.abs(this.balanceAfterNewAdvance()));

  constructor() {
    this.form.controls.employeeId.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(id => {
        this.selectedEmployeeId.set(Number(id));
      });

    this.form.controls.advanceAmountInput.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(raw => {
        const numeric = this.parseCurrencyAmount(raw);
        this.advanceAmountSignal.set(numeric);
      });

    this.form.controls.supervisorOverrideApproved.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(checked => {
        this.supervisorOverrideSignal.set(Boolean(checked));
      });

    this.loadIssuanceContext();
  }

  loadIssuanceContext(): void {
    this.loading.set(true);
    this.service
      .getIssuanceContext()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: ctx => {
          this.employees.set(ctx.employees);
          if (ctx.disbursementSources.length > 0) {
            this.disbursementSources.set(ctx.disbursementSources);
          }

          const kasun =
            ctx.employees.find(e => e.employeeCode === 'EMP-014') ?? ctx.employees[0];
          if (kasun) {
            this.selectedEmployeeId.set(kasun.employeeId);
            this.form.controls.employeeId.setValue(kasun.employeeId, { emitEvent: false });
          }

          if (ctx.suggestedVoucherReference) {
            this.form.controls.voucherReference.setValue(ctx.suggestedVoucherReference);
          }
        },
      });
  }

  onAmountBlur(): void {
    const amount = this.advanceAmountSignal();
    if (amount > 0) {
      this.form.controls.advanceAmountInput.setValue(`Rs. ${this.formatNumber(amount)}`, {
        emitEvent: false,
      });
    }
  }

  toggleSupervisorOverride(): void {
    const next = !this.form.controls.supervisorOverrideApproved.value;
    this.form.controls.supervisorOverrideApproved.setValue(next);
  }

  formatLkr(amount: number): string {
    return `Rs. ${this.formatNumber(Math.abs(amount))}`;
  }

  formatSignedBalanceAfter(): string {
    const val = this.balanceAfterNewAdvance();
    if (val < 0) {
      return `- Rs. ${this.formatNumber(Math.abs(val))}`;
    }
    return `Rs. ${this.formatNumber(val)}`;
  }

  submitAdvance(): void {
    this.form.markAllAsTouched();
    this.error.set('');
    this.success.set('');

    const amount = this.advanceAmountSignal();
    if (amount <= 0) {
      this.error.set('Please enter a valid Advance Amount (LKR) greater than Rs. 0.00.');
      return;
    }

    if (this.form.invalid) {
      this.error.set('Please complete all required salary advance fields marked with *.');
      return;
    }

    if (this.isDeficit() && !this.form.controls.supervisorOverrideApproved.value) {
      this.error.set(
        `This advance creates a - ${this.formatLkr(this.deficitAbsoluteAmount())} payroll deficit. Please check "Supervisor override approved" to authorize this draw.`,
      );
      return;
    }

    this.submitting.set(true);
    const raw = this.form.getRawValue();

    const payload: IssueEmployeeAdvanceRequest = {
      employeeId: Number(raw.employeeId),
      advanceAmount: amount,
      issuanceDate: raw.issuanceDate.trim(),
      disbursementSource: raw.disbursementSource,
      voucherReference: raw.voucherReference.trim().toUpperCase(),
      reason: raw.reason.trim(),
      supervisorOverrideApproved: Boolean(raw.supervisorOverrideApproved),
    };

    this.service
      .issueAdvance(payload)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.submitting.set(false)),
      )
      .subscribe({
        next: res => {
          this.success.set(res.message);
          this.employees.update(list =>
            list.map(emp =>
              emp.employeeId === res.updatedEmployeeProfile.employeeId
                ? res.updatedEmployeeProfile
                : emp,
            ),
          );

          // Advance voucher number for next draw
          const match = /^ADV-2026-(\d+)$/i.exec(raw.voucherReference.trim());
          if (match) {
            const nextNum = Number(match[1]) + 1;
            this.form.controls.voucherReference.setValue(`ADV-2026-${nextNum}`);
          }
        },
        error: (err: unknown) => {
          if (err instanceof HttpErrorResponse && err.error?.message) {
            this.error.set(err.error.message);
          } else {
            this.error.set('Could not issue the salary advance. Please check the form details.');
          }
        },
      });
  }

  resetForm(): void {
    this.error.set('');
    this.success.set('');
    const kasun =
      this.employees().find(e => e.employeeCode === 'EMP-014') ?? this.employees()[0];
    const empId = kasun ? kasun.employeeId : 14;
    this.selectedEmployeeId.set(empId);
    this.advanceAmountSignal.set(25000);
    this.supervisorOverrideSignal.set(true);

    this.form.patchValue({
      employeeId: empId,
      advanceAmountInput: 'Rs. 25,000.00',
      issuanceDate: '13 Nov 2026',
      disbursementSource: this.disbursementSources()[0] ?? 'Cash Vault — Till #1 (Avail: Rs. 164,250)',
      reason:
        'Urgent family hospitalisation bill settlement — employee requested mid-month draw.\nRecovery acknowledged against November & December payroll cycles.',
      supervisorOverrideApproved: true,
    });
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
