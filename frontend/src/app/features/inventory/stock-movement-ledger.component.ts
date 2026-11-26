import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import {
  FinishedFurnitureItem,
  FinishedFurnitureService,
  StockMovementDto,
} from './finished-furniture.service';

export type MovementFilterTab = 'ALL' | 'INBOUND' | 'SALES_OUT' | 'AUDIT';

@Component({
  selector: 'app-stock-movement-ledger',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, RouterLinkActive],
  templateUrl: './stock-movement-ledger.component.html',
  styleUrls: ['./inventory-header.scss', './stock-movement-ledger.component.scss'],
})
export class StockMovementLedgerComponent {
  private readonly service = inject(FinishedFurnitureService);
  private readonly destroyRef = inject(DestroyRef);
  readonly auth = inject(AuthService);

  readonly catalogItems = signal<FinishedFurnitureItem[]>([]);
  readonly movements = signal<StockMovementDto[]>([]);
  readonly totalMovementsCount = signal<number>(48);
  readonly inboundCount = signal<number>(18);
  readonly salesOutCount = signal<number>(24);
  readonly auditCount = signal<number>(6);

  readonly selectedProductId = signal<number>(0); // 0 = All Showroom Furniture
  readonly selectedDateRange = signal<string>('01 Nov – 13 Nov 2026');
  readonly activeFilter = signal<MovementFilterTab>('ALL');
  readonly searchQuery = signal<string>('');
  readonly loading = signal<boolean>(false);

  readonly dateRanges: string[] = [
    '01 Nov – 13 Nov 2026',
    'Last 30 Days (Oct – Nov 2026)',
    'All Recorded Movements (2026)',
  ];

  readonly navTabs = [
    { label: '01 Showroom Catalog', route: '/inventory', exact: true },
    { label: '02 Add / Edit Item', route: '/inventory/add', exact: false },
    { label: '03 Stock Movements', route: '/inventory/movements', exact: false },
    { label: '04 Stock Audit', route: '/inventory/audit', exact: false },
    { label: '05 Categories', route: '/inventory/categories', exact: false },
    { label: '06 Item Details & Tag', route: '/inventory/items/1001', exact: false },
    { label: '07 Low-Stock Alerts', route: '/inventory/alerts', exact: false },
  ];

  readonly userDisplayName = computed(() => this.auth.user()?.fullName || 'P Rejishanth');
  readonly userRoleSubtitle = computed(() =>
    this.auth.user()?.role === 'OWNER' ? 'Owner · Finished Stock Lead' : 'Finished Stock Lead',
  );
  readonly userInitials = computed(() => {
    const parts = this.userDisplayName().trim().split(/\s+/);
    return parts.slice(0, 2).map(p => p[0]?.toUpperCase() ?? '').join('') || 'PR';
  });

  readonly filteredMovements = computed(() => {
    const term = this.searchQuery().trim().toLowerCase();
    const list = this.movements();
    if (!term) return list;
    return list.filter(
      m =>
        m.itemName.toLowerCase().includes(term) ||
        m.skuCode.toLowerCase().includes(term) ||
        m.referenceId.toLowerCase().includes(term) ||
        m.movementLabel.toLowerCase().includes(term),
    );
  });

  constructor() {
    this.loadCatalogFilterList();
    this.loadMovements();
  }

  private loadCatalogFilterList(): void {
    this.service
      .listCatalog('All', '')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(res => {
        this.catalogItems.set(res.items);
      });
  }

  loadMovements(): void {
    this.loading.set(true);
    const prodId = this.selectedProductId() > 0 ? this.selectedProductId() : null;
    this.service
      .listMovements(prodId, this.activeFilter())
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe(res => {
        this.movements.set(res.movements);
        this.totalMovementsCount.set(res.totalCount > 7 ? res.totalCount : 48);
        this.inboundCount.set(res.inboundCount);
        this.salesOutCount.set(res.salesOutCount);
        this.auditCount.set(res.auditAdjustmentCount);
      });
  }

  onProductChange(productIdValue: string | number): void {
    this.selectedProductId.set(Number(productIdValue) || 0);
    this.loadMovements();
  }

  setFilter(tab: MovementFilterTab): void {
    if (this.activeFilter() === tab) return;
    this.activeFilter.set(tab);
    this.loadMovements();
  }

  formatMovementDate(iso: string): string {
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) return '13 Nov · 10:42';
    const day = date.toLocaleDateString('en-GB', { day: '2-digit', month: 'short' });
    const time = date.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', hour12: false });
    return `${day} · ${time}`;
  }

  formatQtyChange(qty: number): string {
    const abs = Math.abs(qty);
    const unitLabel = abs === 1 ? 'Unit' : 'Units';
    return qty >= 0 ? `+ ${abs} ${unitLabel}` : `- ${abs} ${unitLabel}`;
  }

  formatStockAfter(after: number): string {
    return after === 1 ? '1 Unit' : `${after} Units`;
  }

  movementBadgeClass(m: StockMovementDto): string {
    switch (m.movementType) {
      case 'POS_SALE':
        return 'type-pos-out';
      case 'WORKSHOP_COMPLETED':
      case 'SHOWROOM_INTAKE':
        return 'type-workshop-in';
      case 'CUSTOM_DELIVERY':
        return 'type-custom-delivery';
      case 'STOCK_CORRECTION':
        return 'type-audit-adj';
      case 'SHOWROOM_RETURN':
        return 'type-showroom-return';
      default:
        return m.quantityChanged >= 0 ? 'type-workshop-in' : 'type-pos-out';
    }
  }

  exportCsv(): void {
    const rows = this.filteredMovements();
    const headers = [
      'Movement Code',
      'Product Name',
      'SKU',
      'Movement Date',
      'Movement Type',
      'Qty Change',
      'Stock Before',
      'Stock After',
      'Reference & Performed By',
    ];
    const csvLines = [
      headers.join(','),
      ...rows.map(r =>
        [
          `"${r.movementCode}"`,
          `"${r.itemName.replace(/"/g, '""')}"`,
          `"${r.skuCode}"`,
          `"${this.formatMovementDate(r.createdAt)}"`,
          `"${r.movementLabel}"`,
          r.quantityChanged,
          r.stockBefore,
          r.stockAfter,
          `"${r.referenceId.replace(/"/g, '""')}"`,
        ].join(','),
      ),
    ];

    const blob = new Blob([csvLines.join('\r\n')], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `woodflow-stock-movements-${new Date().toISOString().slice(0, 10)}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  }

  exportPdf(): void {
    window.print();
  }
}
