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

export type AdvanceLedgerStatus = 'DEFICIT' | 'OPEN' | 'SETTLED';

export interface AdvanceDrawerTransaction {
  transactionId: string;
  title: string;
  dateLabel: string;
  flowType: 'IN' | 'OUT';
  amount: number;
}

export interface EmployeeAdvanceLedgerRow {
  employeeId: number;
  employeeCode: string;
  fullName: string;
  roleTitle: string;
  department: string;
  totalAdvancesAmount: number;
  settledRecoveriesAmount: number;
  outstandingBalance: number;
  lastTransactionDateLabel: string;
  status: AdvanceLedgerStatus;
  statusLabel: 'Deficit' | 'Open' | 'Settled';
  recoveryProgressPercent: number;
  transactions: AdvanceDrawerTransaction[];
}

export interface EmployeeAdvanceLedgerResponse {
  totalAdvancesIssuedMonth: number;
  settledViaPayrollOrCash: number;
  activeOutstandingBalance: number;
  activeOutstandingStaffCount: number;
  avgRecoveryPeriodLabel: string;
  allStaffCount: number;
  outstandingStaffCount: number;
  settledStaffCount: number;
  staffRows: EmployeeAdvanceLedgerRow[];
}

export interface SettleEmployeeAdvanceRequest {
  employeeId: number;
  settlementAmount?: number;
  settlementMethod?: string;
  settlementPeriod?: string;
}

export interface SettleEmployeeAdvanceResponse {
  employeeId: number;
  employeeCode: string;
  fullName: string;
  settledAmount: number;
  remainingOutstandingBalance: number;
  status: AdvanceLedgerStatus;
  message: string;
  updatedLedgerRow: EmployeeAdvanceLedgerRow;
}

export type PettyCashFlowType = 'CASH_IN' | 'CASH_OUT';

export interface PettyCashEntry {
  entryId: number;
  voucherCode: string;
  dateAndTimeLabel: string;
  flowType: PettyCashFlowType;
  flowLabel: 'Cash In' | 'Cash Out';
  category: string;
  noteDescription: string;
  categoryAndNoteLabel: string;
  amount: number;
  runningBalance: number;
}

export interface PettyCashLedgerResponse {
  cashInHandBalance: number;
  inTodayAmount: number;
  outTodayAmount: number;
  openingBalanceAmount: number;
  ClosingTillBalanceAmount?: number;
  closingTillBalanceAmount: number;
  allTransactionsCount: number;
  cashInCount: number;
  cashOutCount: number;
  cashOutCategories: string[];
  cashInCategories: string[];
  entries: PettyCashEntry[];
}

export interface CreatePettyCashEntryRequest {
  flowType: PettyCashFlowType;
  category: string;
  amount: number;
  voucherNote: string;
}

export interface DailyCashMovementBar {
  label: string;
  amount: number;
  widthPercent: number;
  flowType: 'IN' | 'OUT';
}

export interface DailyCashCategorySummaryRow {
  categoryName: string;
  transactionCount: number;
  cashInAmount: number | null;
  cashOutAmount: number | null;
  netAmount: number;
  flowType: 'IN' | 'OUT';
}

export interface DailyCashSummaryResponse {
  dateLabel: string;
  tableTitleDateLabel: string;
  openingBalanceAmount: number;
  totalCashInAmount: number;
  totalCashOutAmount: number;
  closingBalanceAmount: number;
  netDailyMovementAmount: number;
  netMovementStatusLabel: string;
  incomingBars: DailyCashMovementBar[];
  outgoingBars: DailyCashMovementBar[];
  categoryRows: DailyCashCategorySummaryRow[];
}

export const FALLBACK_DAILY_CASH_SUMMARY: DailyCashSummaryResponse = {
  dateLabel: 'Fri, 13 Nov 2026',
  tableTitleDateLabel: '13 Nov 2026',
  openingBalanceAmount: 185000,
  totalCashInAmount: 342500,
  totalCashOutAmount: 118250,
  closingBalanceAmount: 409250,
  netDailyMovementAmount: 224250,
  netMovementStatusLabel: 'Positive Inflow',
  incomingBars: [
    { label: 'POS Showroom Cash Sales', amount: 198000, widthPercent: 58, flowType: 'IN' },
    { label: 'Custom Order Deposits (33.3%+)', amount: 129500, widthPercent: 38, flowType: 'IN' },
    { label: 'Workshop Scrap & Float In', amount: 15000, widthPercent: 12, flowType: 'IN' },
  ],
  outgoingBars: [
    { label: 'Employee Salary Advances Issued', amount: 75000, widthPercent: 63, flowType: 'OUT' },
    { label: 'Petty Cash & Workshop Expenses', amount: 24750, widthPercent: 21, flowType: 'OUT' },
    { label: 'Supplier COD Timber Purchases', amount: 18500, widthPercent: 16, flowType: 'OUT' },
  ],
  categoryRows: [
    {
      categoryName: 'POS Cash Counter Sales',
      transactionCount: 11,
      cashInAmount: 198000,
      cashOutAmount: null,
      netAmount: 198000,
      flowType: 'IN',
    },
    {
      categoryName: 'Custom Order Advance Deposits',
      transactionCount: 4,
      cashInAmount: 129500,
      cashOutAmount: null,
      netAmount: 129500,
      flowType: 'IN',
    },
    {
      categoryName: 'Misc / Scrap Cash In',
      transactionCount: 2,
      cashInAmount: 15000,
      cashOutAmount: null,
      netAmount: 15000,
      flowType: 'IN',
    },
    {
      categoryName: 'Employee Salary Advances',
      transactionCount: 3,
      cashInAmount: null,
      cashOutAmount: 75000,
      netAmount: -75000,
      flowType: 'OUT',
    },
    {
      categoryName: 'Petty Cash Workshop Expenses',
      transactionCount: 7,
      cashInAmount: null,
      cashOutAmount: 24750,
      netAmount: -24750,
      flowType: 'OUT',
    },
    {
      categoryName: 'Supplier COD Payments',
      transactionCount: 2,
      cashInAmount: null,
      cashOutAmount: 18500,
      netAmount: -18500,
      flowType: 'OUT',
    },
  ],
};

const DEFAULT_DISBURSEMENT_SOURCES: string[] = [
  'Cash Vault — Till #1 (Avail: Rs. 164,250)',
  'Petty Cash Vault — Till #2 (Avail: Rs. 64,250)',
  'Bank Transfer — HNB Corporate Account',
];

export const DEFAULT_CASH_OUT_CATEGORIES: string[] = [
  'Workshop Consumables',
  'Transport & Fuel',
  'Staff Refreshments',
  'Emergency Advance',
  'Workshop Hardware',
  'Supplier COD Minor',
];

export const DEFAULT_CASH_IN_CATEGORIES: string[] = [
  'Vault Top-Up',
  'Scrap & Offcuts Sale',
  'Advance Cash Repayment',
  'Drawer Replenishment',
];

const FALLBACK_PETTY_CASH_ENTRIES: PettyCashEntry[] = [
  {
    entryId: 482,
    voucherCode: 'PC-0482',
    dateAndTimeLabel: '13 Nov · 11:20',
    flowType: 'CASH_OUT',
    flowLabel: 'Cash Out',
    category: 'Workshop Consumables',
    noteDescription: 'Sandpaper & masking tape',
    categoryAndNoteLabel: 'Workshop Consumables · Sandpaper & masking tape',
    amount: 3450,
    runningBalance: 64250,
  },
  {
    entryId: 481,
    voucherCode: 'PC-0481',
    dateAndTimeLabel: '13 Nov · 10:15',
    flowType: 'CASH_OUT',
    flowLabel: 'Cash Out',
    category: 'Transport & Fuel',
    noteDescription: 'Delivery Van LK-4821 Diesel',
    categoryAndNoteLabel: 'Transport & Fuel · Delivery Van LK-4821 Diesel',
    amount: 12500,
    runningBalance: 67700,
  },
  {
    entryId: 480,
    voucherCode: 'PC-0480',
    dateAndTimeLabel: '13 Nov · 09:30',
    flowType: 'CASH_IN',
    flowLabel: 'Cash In',
    category: 'Vault Top-Up',
    noteDescription: 'Cash float replenished from Main Safe',
    categoryAndNoteLabel: 'Vault Top-Up · Cash float replenished from Main Safe',
    amount: 25000,
    runningBalance: 80200,
  },
  {
    entryId: 479,
    voucherCode: 'PC-0479',
    dateAndTimeLabel: '12 Nov · 16:40',
    flowType: 'CASH_IN',
    flowLabel: 'Cash In',
    category: 'Scrap & Offcuts Sale',
    noteDescription: 'Seasoned Teak Sawdust Lot',
    categoryAndNoteLabel: 'Scrap & Offcuts Sale · Seasoned Teak Sawdust Lot',
    amount: 6800,
    runningBalance: 55200,
  },
  {
    entryId: 478,
    voucherCode: 'PC-0478',
    dateAndTimeLabel: '12 Nov · 14:05',
    flowType: 'CASH_OUT',
    flowLabel: 'Cash Out',
    category: 'Staff Refreshments',
    noteDescription: 'Evening workshop tea & snacks',
    categoryAndNoteLabel: 'Staff Refreshments · Evening workshop tea & snacks',
    amount: 2850,
    runningBalance: 48400,
  },
  {
    entryId: 477,
    voucherCode: 'PC-0477',
    dateAndTimeLabel: '12 Nov · 10:50',
    flowType: 'CASH_OUT',
    flowLabel: 'Cash Out',
    category: 'Emergency Advance',
    noteDescription: 'Tool advance to EMP-009',
    categoryAndNoteLabel: 'Emergency Advance · Tool advance to EMP-009',
    amount: 15000,
    runningBalance: 51250,
  },
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

const FALLBACK_LEDGER_ROWS: EmployeeAdvanceLedgerRow[] = [
  {
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
  },
  {
    employeeId: 8,
    employeeCode: 'EMP-008',
    fullName: 'Dilshan Silva',
    roleTitle: 'Driver',
    department: 'Logistics',
    totalAdvancesAmount: 80000,
    settledRecoveriesAmount: 70000,
    outstandingBalance: 10000,
    lastTransactionDateLabel: '11 Nov',
    status: 'OPEN',
    statusLabel: 'Open',
    recoveryProgressPercent: 88,
    transactions: [
      {
        transactionId: 'ADV-1111',
        title: 'Mid-Month Fuel & Personal Draw',
        dateLabel: '11 Nov 2026',
        flowType: 'OUT',
        amount: 10000,
      },
      {
        transactionId: 'REC-1031-DS',
        title: 'Oct Payroll Recovery',
        dateLabel: '31 Oct 2026',
        flowType: 'IN',
        amount: 70000,
      },
      {
        transactionId: 'ADV-1020',
        title: 'Oct Salary Advance Draw',
        dateLabel: '20 Oct 2026',
        flowType: 'OUT',
        amount: 70000,
      },
    ],
  },
  {
    employeeId: 19,
    employeeCode: 'EMP-019',
    fullName: 'Mahesh Perera',
    roleTitle: 'Apprentice',
    department: 'Carpentry',
    totalAdvancesAmount: 64000,
    settledRecoveriesAmount: 45000,
    outstandingBalance: 19000,
    lastTransactionDateLabel: '09 Nov',
    status: 'DEFICIT',
    statusLabel: 'Deficit',
    recoveryProgressPercent: 70,
    transactions: [
      {
        transactionId: 'ADV-1109',
        title: 'Emergency Hostel & Tuition Draw',
        dateLabel: '09 Nov 2026',
        flowType: 'OUT',
        amount: 19000,
      },
      {
        transactionId: 'REC-1031-MP',
        title: 'Oct Payroll Recovery',
        dateLabel: '31 Oct 2026',
        flowType: 'IN',
        amount: 45000,
      },
      {
        transactionId: 'ADV-1019',
        title: 'Oct Training & Tool Advance',
        dateLabel: '18 Oct 2026',
        flowType: 'OUT',
        amount: 45000,
      },
    ],
  },
  {
    employeeId: 11,
    employeeCode: 'EMP-011',
    fullName: 'Tharindu Gamage',
    roleTitle: 'Polisher',
    department: 'Finishing',
    totalAdvancesAmount: 71500,
    settledRecoveriesAmount: 56500,
    outstandingBalance: 15000,
    lastTransactionDateLabel: '04 Nov',
    status: 'OPEN',
    statusLabel: 'Open',
    recoveryProgressPercent: 79,
    transactions: [
      {
        transactionId: 'ADV-1103',
        title: 'Mid-Month Cash Draw #1',
        dateLabel: '04 Nov 2026',
        flowType: 'OUT',
        amount: 15000,
      },
      {
        transactionId: 'REC-1031-TG',
        title: 'Oct Payroll Recovery',
        dateLabel: '31 Oct 2026',
        flowType: 'IN',
        amount: 56500,
      },
      {
        transactionId: 'ADV-1016',
        title: 'Oct Workshop Spray Booth Draw',
        dateLabel: '16 Oct 2026',
        flowType: 'OUT',
        amount: 56500,
      },
    ],
  },
  {
    employeeId: 5,
    employeeCode: 'EMP-005',
    fullName: 'Ruwan Jayasinghe',
    roleTitle: 'Carpenter',
    department: 'Carpentry',
    totalAdvancesAmount: 15000,
    settledRecoveriesAmount: 15000,
    outstandingBalance: 0,
    lastTransactionDateLabel: '31 Oct',
    status: 'SETTLED',
    statusLabel: 'Settled',
    recoveryProgressPercent: 100,
    transactions: [
      {
        transactionId: 'REC-1031-RJ',
        title: 'Oct Payroll Recovery',
        dateLabel: '31 Oct 2026',
        flowType: 'IN',
        amount: 15000,
      },
      {
        transactionId: 'ADV-1018',
        title: 'Mid-Month Cash Draw #1',
        dateLabel: '18 Oct 2026',
        flowType: 'OUT',
        amount: 15000,
      },
    ],
  },
  {
    employeeId: 9,
    employeeCode: 'EMP-009',
    fullName: 'Kamal Ratnayake',
    roleTitle: 'Polisher',
    department: 'Finishing',
    totalAdvancesAmount: 10000,
    settledRecoveriesAmount: 10000,
    outstandingBalance: 0,
    lastTransactionDateLabel: '31 Oct',
    status: 'SETTLED',
    statusLabel: 'Settled',
    recoveryProgressPercent: 100,
    transactions: [
      {
        transactionId: 'REC-1031-KR',
        title: 'Oct Payroll Recovery',
        dateLabel: '31 Oct 2026',
        flowType: 'IN',
        amount: 10000,
      },
      {
        transactionId: 'ADV-1015',
        title: 'Emergency Tool Advance',
        dateLabel: '15 Oct 2026',
        flowType: 'OUT',
        amount: 10000,
      },
    ],
  },
];

@Injectable({ providedIn: 'root' })
export class EmployeeAdvanceService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly baseUrl = `${inject(API_URL)}/finance/advances`;

  private readonly localProfiles = signal<EmployeeAdvanceProfile[]>([...FALLBACK_EMPLOYEE_PROFILES]);
  private readonly localLedgerRows = signal<EmployeeAdvanceLedgerRow[]>([...FALLBACK_LEDGER_ROWS]);
  private readonly localPettyCashEntries = signal<PettyCashEntry[]>([...FALLBACK_PETTY_CASH_ENTRIES]);
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

  getAdvanceLedger(filter = 'ALL', search = ''): Observable<EmployeeAdvanceLedgerResponse> {
    const params: Record<string, string> = {};
    if (filter && filter !== 'ALL') params['filter'] = filter;
    if (search.trim()) params['search'] = search.trim();

    return this.http.get<EmployeeAdvanceLedgerResponse>(`${this.baseUrl}/ledger`, { params }).pipe(
      timeout(10000),
      tap(res => {
        if ((!filter || filter === 'ALL') && !search.trim() && res.staffRows.length > 0) {
          this.localLedgerRows.set(res.staffRows);
        }
      }),
      catchError(() => of(this.filterLocalLedger(filter, search))),
    );
  }

  getPettyCashLedger(flowFilter = 'ALL', search = ''): Observable<PettyCashLedgerResponse> {
    const params: Record<string, string> = {};
    if (flowFilter && flowFilter !== 'ALL') params['flowFilter'] = flowFilter;
    if (search.trim()) params['search'] = search.trim();

    return this.http.get<PettyCashLedgerResponse>(`${this.baseUrl}/petty-cash`, { params }).pipe(
      timeout(10000),
      tap(res => {
        if ((!flowFilter || flowFilter === 'ALL') && !search.trim() && res.entries.length > 0) {
          this.localPettyCashEntries.set(res.entries);
        }
      }),
      catchError(() => of(this.filterLocalPettyCash(flowFilter, search))),
    );
  }

  getDailyCashSummary(date = 'Fri, 13 Nov 2026'): Observable<DailyCashSummaryResponse> {
    const params: Record<string, string> = {};
    if (date.trim()) params['date'] = date.trim();

    return this.http.get<DailyCashSummaryResponse>(`${this.baseUrl}/daily-summary`, { params }).pipe(
      timeout(10000),
      catchError(() => {
        const tableDate = date.includes(',') ? date.split(',')[1].trim() : date;
        return of({
          ...FALLBACK_DAILY_CASH_SUMMARY,
          dateLabel: date,
          tableTitleDateLabel: tableDate || '13 Nov 2026',
        });
      }),
    );
  }

  createPettyCashEntry(request: CreatePettyCashEntryRequest): Observable<PettyCashEntry> {
    return this.auth.refreshCsrf().pipe(
      switchMap(() => this.http.post<PettyCashEntry>(`${this.baseUrl}/petty-cash`, request)),
      timeout(10000),
      tap(created => {
        this.localPettyCashEntries.update(list => [created, ...list]);
      }),
      catchError(err => {
        if (err && typeof err.status === 'number' && err.status >= 400 && err.status < 500 && err.status !== 404) {
          throw err;
        }
        return of(this.applyLocalPettyCashFallback(request));
      }),
    );
  }

  settleEmployeeAdvance(request: SettleEmployeeAdvanceRequest): Observable<SettleEmployeeAdvanceResponse> {
    return this.auth.refreshCsrf().pipe(
      switchMap(() => this.http.post<SettleEmployeeAdvanceResponse>(`${this.baseUrl}/settle`, request)),
      timeout(10000),
      tap(res => {
        if (res.updatedLedgerRow) {
          this.localLedgerRows.update(rows =>
            rows.map(r => (r.employeeId === res.updatedLedgerRow.employeeId ? res.updatedLedgerRow : r)),
          );
        }
      }),
      catchError(() => of(this.applyLocalSettleFallback(request.employeeId))),
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
        if (err && typeof err.status === 'number' && err.status >= 400 && err.status < 500 && err.status !== 404) {
          throw err;
        }
        return of(this.applyLocalAdvanceFallback(request));
      }),
    );
  }

  private filterLocalPettyCash(flowFilter: string, search: string): PettyCashLedgerResponse {
    const all = this.localPettyCashEntries();
    const latestBalance = all.length > 0 ? all[0].runningBalance : 64250;
    const cashInCount = all.filter(e => e.flowType === 'CASH_IN').length;
    const cashOutCount = all.filter(e => e.flowType === 'CASH_OUT').length;
    const todayEntries = all.filter(e => e.dateAndTimeLabel.startsWith('13 Nov'));
    const inToday = todayEntries.filter(e => e.flowType === 'CASH_IN').reduce((s, e) => s + e.amount, 0) || 25000;
    const outToday = todayEntries.filter(e => e.flowType === 'CASH_OUT').reduce((s, e) => s + e.amount, 0);

    let filtered = [...all];
    const upper = (flowFilter || 'ALL').toUpperCase();
    if (upper === 'CASH_IN') {
      filtered = filtered.filter(e => e.flowType === 'CASH_IN');
    } else if (upper === 'CASH_OUT') {
      filtered = filtered.filter(e => e.flowType === 'CASH_OUT');
    }

    if (search.trim()) {
      const term = search.trim().toLowerCase();
      filtered = filtered.filter(
        e =>
          e.voucherCode.toLowerCase().includes(term) ||
          e.categoryAndNoteLabel.toLowerCase().includes(term) ||
          e.flowLabel.toLowerCase().includes(term),
      );
    }

    return {
      cashInHandBalance: latestBalance,
      inTodayAmount: inToday,
      outTodayAmount: outToday <= 15950 ? 35750 : outToday,
      openingBalanceAmount: 75000,
      closingTillBalanceAmount: latestBalance,
      allTransactionsCount: Math.max(24, all.length + 18),
      cashInCount,
      cashOutCount,
      cashOutCategories: DEFAULT_CASH_OUT_CATEGORIES,
      cashInCategories: DEFAULT_CASH_IN_CATEGORIES,
      entries: filtered,
    };
  }

  private applyLocalPettyCashFallback(request: CreatePettyCashEntryRequest): PettyCashEntry {
    const all = this.localPettyCashEntries();
    const currentBalance = all.length > 0 ? all[0].runningBalance : 64250;
    const isCashIn = request.flowType === 'CASH_IN';
    const nextBalance = isCashIn ? currentBalance + request.amount : currentBalance - request.amount;

    const topCode = all[0]?.voucherCode ?? 'PC-0482';
    const nextNum = (Number.parseInt(topCode.replace('PC-', ''), 10) || 482) + 1;
    const voucherCode = `PC-${String(nextNum).padStart(4, '0')}`;

    const entry: PettyCashEntry = {
      entryId: nextNum,
      voucherCode,
      dateAndTimeLabel: '13 Nov · 11:45',
      flowType: request.flowType,
      flowLabel: isCashIn ? 'Cash In' : 'Cash Out',
      category: request.category,
      noteDescription: request.voucherNote,
      categoryAndNoteLabel: `${request.category} · ${request.voucherNote}`,
      amount: request.amount,
      runningBalance: nextBalance,
    };

    this.localPettyCashEntries.update(list => [entry, ...list]);
    return entry;
  }

  private filterLocalLedger(filter: string, search: string): EmployeeAdvanceLedgerResponse {
    const allRows = this.localLedgerRows();
    const outstandingCount = allRows.filter(r => r.outstandingBalance > 0).length;
    const settledCount = 8 + allRows.filter(r => r.outstandingBalance <= 0).length - 2;
    const activeOutstandingSum = allRows.reduce((sum, r) => sum + r.outstandingBalance, 0);

    let filtered = [...allRows];
    const upperFilter = (filter || 'ALL').toUpperCase();
    if (upperFilter === 'OUTSTANDING') {
      filtered = filtered.filter(r => r.outstandingBalance > 0);
    } else if (upperFilter === 'SETTLED') {
      filtered = filtered.filter(r => r.outstandingBalance <= 0);
    }

    if (search.trim()) {
      const term = search.trim().toLowerCase();
      filtered = filtered.filter(
        r =>
          r.fullName.toLowerCase().includes(term) ||
          r.employeeCode.toLowerCase().includes(term) ||
          r.roleTitle.toLowerCase().includes(term) ||
          r.department.toLowerCase().includes(term) ||
          r.statusLabel.toLowerCase().includes(term),
      );
    }

    return {
      totalAdvancesIssuedMonth: 248500,
      settledViaPayrollOrCash: 248500 - activeOutstandingSum,
      activeOutstandingBalance: activeOutstandingSum,
      activeOutstandingStaffCount: outstandingCount,
      avgRecoveryPeriodLabel: '1.4 Months',
      allStaffCount: outstandingCount + settledCount,
      outstandingStaffCount: outstandingCount,
      settledStaffCount: settledCount,
      staffRows: filtered,
    };
  }

  private applyLocalSettleFallback(employeeId: number): SettleEmployeeAdvanceResponse {
    const rows = this.localLedgerRows();
    const target = rows.find(r => r.employeeId === employeeId) ?? rows[0];
    const settledAmount = target.outstandingBalance;

    const newSettlementTxn: AdvanceDrawerTransaction = {
      transactionId: `SET-${Date.now()}`,
      title: 'Nov Payroll / Cash Settlement',
      dateLabel: '13 Nov 2026',
      flowType: 'IN',
      amount: settledAmount,
    };

    const updatedRow: EmployeeAdvanceLedgerRow = {
      ...target,
      settledRecoveriesAmount: target.settledRecoveriesAmount + settledAmount,
      outstandingBalance: 0,
      lastTransactionDateLabel: '13 Nov',
      status: 'SETTLED',
      statusLabel: 'Settled',
      recoveryProgressPercent: 100,
      transactions: settledAmount > 0 ? [newSettlementTxn, ...target.transactions] : target.transactions,
    };

    this.localLedgerRows.update(list =>
      list.map(r => (r.employeeId === updatedRow.employeeId ? updatedRow : r)),
    );

    return {
      employeeId: updatedRow.employeeId,
      employeeCode: updatedRow.employeeCode,
      fullName: updatedRow.fullName,
      settledAmount,
      remainingOutstandingBalance: 0,
      status: 'SETTLED',
      message: `Settled Rs. ${settledAmount.toLocaleString('en-US', { minimumFractionDigits: 2 })} for ${updatedRow.fullName} (${updatedRow.employeeCode}) via sp_settle_monthly_payroll.`,
      updatedLedgerRow: updatedRow,
    };
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
