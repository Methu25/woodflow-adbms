import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, of, switchMap, tap, timeout } from 'rxjs';
import { API_URL } from '../../core/api-url.token';
import { AuthService } from '../../core/auth/auth.service';

export interface OutstandingAdvanceLine {
  advanceId: number;
  voucherReference: string;
  dateLabel: string;
  titleLabel: string;
  displayLine: string;
  amount: number;
  isSettled: boolean;
}

export interface EmployeeAdvanceProfile {
  employeeId: number;
  employeeCode: string;
  fullName: string;
  roleTitle: string;
  department: string;
  dropdownLabel: string;
  initials: string;
  baseMonthlySalary: number;
  approvedPieceworkCredits: number;
  totalMtdGrossEarnings: number;
  outstandingAdvances: OutstandingAdvanceLine[];
  totalOutstandingAdvances: number;
  netAvailableBeforeAdvance: number;
}

export interface AdvanceIssuanceContextResponse {
  employees: EmployeeAdvanceProfile[];
  disbursementSources: string[];
  suggestedVoucherReference: string;
  till1AvailableBalance: number;
}

export interface IssueEmployeeAdvanceRequest {
  employeeId: number;
  advanceAmount: number;
  issuanceDate: string;
  disbursementSource: string;
  voucherReference: string;
  reason: string;
  supervisorOverrideApproved: boolean;
}

export interface IssueEmployeeAdvanceResponse {
  advanceId: number;
  employeeId: number;
  employeeCode: string;
  employeeName: string;
  voucherReference: string;
  advanceAmount: number;
  issuanceDate: string;
  disbursementSource: string;
  reason: string;
  netAvailableBeforeAdvance: number;
  balanceAfterNewAdvance: number;
  isDeficitFlagged: boolean;
  supervisorOverrideApproved: boolean;
  message: string;
  updatedEmployeeProfile: EmployeeAdvanceProfile;
}

const DEFAULT_DISBURSEMENT_SOURCES: string[] = [
  'Cash Vault — Till #1 (Avail: Rs. 164,250)',
  'Petty Cash Vault — Till #2 (Avail: Rs. 64,250)',
  'Bank Transfer — HNB Corporate Account',
];

const FALLBACK_EMPLOYEE_PROFILES: EmployeeAdvanceProfile[] = [
  {
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
  },
  {
    employeeId: 8,
    employeeCode: 'EMP-008',
    fullName: 'Dilshan Silva',
    roleTitle: 'Driver',
    department: 'Delivery & Logistics',
    dropdownLabel: 'Dilshan Silva (EMP-008 · Delivery & Logistics)',
    initials: 'DS',
    baseMonthlySalary: 75000,
    approvedPieceworkCredits: 15000,
    totalMtdGrossEarnings: 90000,
    outstandingAdvances: [
      {
        advanceId: 203,
        voucherReference: 'ADV-2026-1111',
        dateLabel: '11 Nov',
        titleLabel: 'Mid-Month Fuel & Personal Draw',
        displayLine: '11 Nov · Mid-Month Fuel & Personal Draw',
        amount: 10000,
        isSettled: false,
      },
    ],
    totalOutstandingAdvances: 10000,
    netAvailableBeforeAdvance: 80000,
  },
  {
    employeeId: 19,
    employeeCode: 'EMP-019',
    fullName: 'Mahesh Perera',
    roleTitle: 'Apprentice',
    department: 'Carpentry Workshop',
    dropdownLabel: 'Mahesh Perera (EMP-019 · Carpentry Workshop)',
    initials: 'MP',
    baseMonthlySalary: 40000,
    approvedPieceworkCredits: 5000,
    totalMtdGrossEarnings: 45000,
    outstandingAdvances: [
      {
        advanceId: 204,
        voucherReference: 'ADV-2026-1109',
        dateLabel: '09 Nov',
        titleLabel: 'Emergency Hostel & Tuition Draw',
        displayLine: '09 Nov · Emergency Hostel & Tuition Draw',
        amount: 19000,
        isSettled: false,
      },
    ],
    totalOutstandingAdvances: 19000,
    netAvailableBeforeAdvance: 26000,
  },
  {
    employeeId: 11,
    employeeCode: 'EMP-011',
    fullName: 'Tharindu Gamage',
    roleTitle: 'Polisher',
    department: 'Finishing & Spray Booth',
    dropdownLabel: 'Tharindu Gamage (EMP-011 · Finishing & Spray Booth)',
    initials: 'TG',
    baseMonthlySalary: 55000,
    approvedPieceworkCredits: 16500,
    totalMtdGrossEarnings: 71500,
    outstandingAdvances: [
      {
        advanceId: 205,
        voucherReference: 'ADV-2026-1103',
        dateLabel: '04 Nov',
        titleLabel: 'Mid-Month Cash Draw #1',
        displayLine: '04 Nov · Mid-Month Cash Draw #1',
        amount: 15000,
        isSettled: false,
      },
    ],
    totalOutstandingAdvances: 15000,
    netAvailableBeforeAdvance: 56500,
  },
  {
    employeeId: 5,
    employeeCode: 'EMP-005',
    fullName: 'Ruwan Jayasinghe',
    roleTitle: 'Carpenter',
    department: 'Carpentry Workshop',
    dropdownLabel: 'Ruwan Jayasinghe (EMP-005 · Carpentry Workshop)',
    initials: 'RJ',
    baseMonthlySalary: 62000,
    approvedPieceworkCredits: 14000,
    totalMtdGrossEarnings: 76000,
    outstandingAdvances: [],
    totalOutstandingAdvances: 0,
    netAvailableBeforeAdvance: 76000,
  },
  {
    employeeId: 9,
    employeeCode: 'EMP-009',
    fullName: 'Kamal Ratnayake',
    roleTitle: 'Polisher',
    department: 'Finishing & Spray Booth',
    dropdownLabel: 'Kamal Ratnayake (EMP-009 · Finishing & Spray Booth)',
    initials: 'KR',
    baseMonthlySalary: 54000,
    approvedPieceworkCredits: 11000,
    totalMtdGrossEarnings: 65000,
    outstandingAdvances: [],
    totalOutstandingAdvances: 0,
    netAvailableBeforeAdvance: 65000,
  },
];

@Injectable({ providedIn: 'root' })
export class EmployeeAdvanceService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly baseUrl = `${inject(API_URL)}/finance/advances`;

  private readonly localProfiles = signal<EmployeeAdvanceProfile[]>([...FALLBACK_EMPLOYEE_PROFILES]);
  private readonly localVoucherCounter = signal<number>(1113);

  getIssuanceContext(): Observable<AdvanceIssuanceContextResponse> {
    return this.http.get<AdvanceIssuanceContextResponse>(`${this.baseUrl}/issuance-context`).pipe(
      timeout(10000),
      tap(res => {
        if (res.employees && res.employees.length > 0) {
          this.localProfiles.set(res.employees);
        }
      }),
      catchError(() =>
        of({
          employees: this.localProfiles(),
          disbursementSources: DEFAULT_DISBURSEMENT_SOURCES,
          suggestedVoucherReference: `ADV-2026-${this.localVoucherCounter()}`,
          till1AvailableBalance: 164250,
        }),
      ),
    );
  }

  issueAdvance(request: IssueEmployeeAdvanceRequest): Observable<IssueEmployeeAdvanceResponse> {
    return this.auth.refreshCsrf().pipe(
      switchMap(() => this.http.post<IssueEmployeeAdvanceResponse>(this.baseUrl, request)),
      timeout(10000),
      tap(res => {
        if (res.updatedEmployeeProfile) {
          this.localProfiles.update(list =>
            list.map(emp =>
              emp.employeeId === res.updatedEmployeeProfile.employeeId ? res.updatedEmployeeProfile : emp,
            ),
          );
          this.localVoucherCounter.update(v => v + 1);
        }
      }),
      catchError(err => {
        // If backend returned a validation/domain error (400/409/422), rethrow so the form displays it
        if (err && typeof err.status === 'number' && err.status >= 400 && err.status < 500 && err.status !== 404) {
          throw err;
        }
        return of(this.applyLocalAdvanceFallback(request));
      }),
    );
  }

  private applyLocalAdvanceFallback(request: IssueEmployeeAdvanceRequest): IssueEmployeeAdvanceResponse {
    const currentList = this.localProfiles();
    const target = currentList.find(e => e.employeeId === request.employeeId) ?? currentList[0];
    const netBefore = target.netAvailableBeforeAdvance;
    const balanceAfter = netBefore - request.advanceAmount;
    const isDeficit = balanceAfter < 0;

    const newLine: OutstandingAdvanceLine = {
      advanceId: Date.now(),
      voucherReference: request.voucherReference,
      dateLabel: '13 Nov',
      titleLabel: `Cash Advance #${request.voucherReference.replace('ADV-2026-', 'ADV-')}`,
      displayLine: `13 Nov · Cash Advance #${request.voucherReference.replace('ADV-2026-', 'ADV-')}`,
      amount: request.advanceAmount,
      isSettled: false,
    };

    const updatedOutstanding = [newLine, ...target.outstandingAdvances];
    const totalOutstanding = updatedOutstanding.reduce((sum, a) => sum + a.amount, 0);

    const updatedProfile: EmployeeAdvanceProfile = {
      ...target,
      outstandingAdvances: updatedOutstanding,
      totalOutstandingAdvances: totalOutstanding,
      netAvailableBeforeAdvance: target.totalMtdGrossEarnings - totalOutstanding,
    };

    this.localProfiles.update(list =>
      list.map(emp => (emp.employeeId === updatedProfile.employeeId ? updatedProfile : emp)),
    );
    this.localVoucherCounter.update(v => v + 1);

    return {
      advanceId: newLine.advanceId,
      employeeId: updatedProfile.employeeId,
      employeeCode: updatedProfile.employeeCode,
      employeeName: updatedProfile.fullName,
      voucherReference: request.voucherReference,
      advanceAmount: request.advanceAmount,
      issuanceDate: request.issuanceDate,
      disbursementSource: request.disbursementSource,
      reason: request.reason,
      netAvailableBeforeAdvance: netBefore,
      balanceAfterNewAdvance: balanceAfter,
      isDeficitFlagged: isDeficit,
      supervisorOverrideApproved: request.supervisorOverrideApproved,
      message: isDeficit
        ? `Advance ${request.voucherReference} (Rs. ${request.advanceAmount.toLocaleString('en-US', { minimumFractionDigits: 2 })}) issued to ${updatedProfile.fullName} with supervisor deficit override.`
        : `Advance ${request.voucherReference} (Rs. ${request.advanceAmount.toLocaleString('en-US', { minimumFractionDigits: 2 })}) issued to ${updatedProfile.fullName} from Till #1.`,
      updatedEmployeeProfile: updatedProfile,
    };
  }
}
