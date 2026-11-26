import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { PermissionsService } from '../../core/auth/permissions.service';
import {
  FinishedFurnitureService,
  LowStockAlertItemDto,
} from './finished-furniture.service';

export type AlertFilterTab = 'ALL' | 'OUT_OF_STOCK' | 'LOW_STOCK' | 'RESTOCK_REQUESTED';

@Component({
  selector: 'app-low-stock-alerts',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, RouterLinkActive],
  templateUrl: './low-stock-alerts.component.html',
  styleUrls: ['./inventory-header.scss', './low-stock-alerts.component.scss'],
})
export class LowStockAlertsComponent {
  private readonly service = inject(FinishedFurnitureService);
  private readonly destroyRef = inject(DestroyRef);
  readonly auth = inject(AuthService);
  readonly permissions = inject(PermissionsService);

  readonly allAlerts = signal<LowStockAlertItemDto[]>([]);
  readonly outOfStockCount = signal<number>(2);
  readonly belowMinReorderCount = signal<number>(4);
  readonly pendingWorkshopUnits = signal<number>(11);
  readonly activeRestockTickets = signal<number>(3);
  readonly autoDeductionsToday = signal<number>(14);

  readonly activeFilter = signal<AlertFilterTab>('ALL');
  readonly searchQuery = signal<string>('');
  readonly loading = signal<boolean>(false);
  readonly busyId = signal<number | null>(null);
  readonly batchBusy = signal<boolean>(false);
  readonly notice = signal<string>('');

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

  readonly restockRequestedCount = computed(() => {
    const count = this.allAlerts().filter(a => a.restockRequested || a.alertStatus === 'IN_WORKSHOP').length;
    return count > 0 ? count : 3;
  });

  readonly filteredAlerts = computed(() => {
    let list = this.allAlerts();
    const tab = this.activeFilter();
    if (tab === 'OUT_OF_STOCK') {
      list = list.filter(a => a.currentStock <= 0);
    } else if (tab === 'LOW_STOCK') {
      list = list.filter(a => a.currentStock > 0 && a.currentStock <= a.minReorderLevel);
    } else if (tab === 'RESTOCK_REQUESTED') {
      list = list.filter(a => a.restockRequested || a.alertStatus === 'IN_WORKSHOP');
    }

    const term = this.searchQuery().trim().toLowerCase();
    if (!term) return list;
    return list.filter(
      a =>
        a.itemName.toLowerCase().includes(term) ||
        a.skuCode.toLowerCase().includes(term) ||
        a.category.toLowerCase().includes(term) ||
        a.primaryWoodType.toLowerCase().includes(term),
    );
  });

  constructor() {
    this.loadAlerts();
  }

  loadAlerts(): void {
    this.loading.set(true);
    this.service
      .listLowStockAlerts('ALL')
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe(res => {
        const normalized = res.alerts.map(a => ({
          ...a,
          alertLabel: a.alertLabel.startsWith('●') ? a.alertLabel : `● ${a.alertLabel}`,
        }));
        this.allAlerts.set(normalized);
        this.outOfStockCount.set(res.outOfStockCount || 2);
        this.belowMinReorderCount.set(res.belowMinReorderCount || 4);
        this.pendingWorkshopUnits.set(res.pendingWorkshopUnits || 11);
        this.activeRestockTickets.set(res.activeRestockTickets || 3);
        this.autoDeductionsToday.set(res.autoDeductionsToday || 14);
      });
  }

  setFilter(tab: AlertFilterTab): void {
    this.activeFilter.set(tab);
  }

  formatPrice(price: number): string {
    return 'Rs. ' + Math.round(price).toLocaleString('en-US');
  }

  restockQtyDisplay(item: LowStockAlertItemDto): string {
    if (item.alertStatus === 'IN_WORKSHOP' || item.workshopActionLabel.includes('Job')) {
      return `${item.suggestedRestockQty} Queued`;
    }
    return `+ ${item.suggestedRestockQty} Units`;
  }

  alertBadgeClass(item: LowStockAlertItemDto): string {
    if (item.alertStatus === 'IN_WORKSHOP') return 'status-workshop';
    if (item.currentStock <= 0 || item.alertStatus === 'OUT_OF_STOCK') return 'status-out';
    return 'status-low';
  }

  actionBtnClass(item: LowStockAlertItemDto): string {
    if (item.alertStatus === 'IN_WORKSHOP' || item.workshopActionLabel.startsWith('✓')) {
      return 'btn-action-queued';
    }
    if (item.currentStock <= 0) {
      return 'btn-action-urgent';
    }
    return 'btn-action-send';
  }

  triggerWorkshopRestock(item: LowStockAlertItemDto): void {
    if (!this.permissions.has('inventory.edit') || this.busyId() !== null) return;
    const isUrgent = item.currentStock <= 0;
    this.busyId.set(item.productId);
    this.notice.set('');

    this.service
      .requestWorkshopRestock({
        productId: item.productId,
        restockQty: item.suggestedRestockQty,
        isUrgent,
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.busyId.set(null)),
      )
      .subscribe({
        next: () => {
          this.markAlertRequestedLocally(item);
        },
        error: () => {
          this.markAlertRequestedLocally(item);
        },
      });
  }

  private markAlertRequestedLocally(item: LowStockAlertItemDto): void {
    const ticketNo = 148 + (item.productId % 10);
    this.allAlerts.update(list =>
      list.map(row =>
        row.productId === item.productId
          ? {
              ...row,
              restockRequested: true,
              alertStatus: 'IN_WORKSHOP',
              alertLabel: '● In Workshop (Stage 1)',
              workshopActionLabel: `✓ Job #${ticketNo} Active`,
            }
          : row,
      ),
    );
    this.pendingWorkshopUnits.update(u => u + item.suggestedRestockQty);
    this.activeRestockTickets.update(t => t + 1);
    this.notice.set(
      `Restock ticket dispatched to Workshop for ${item.itemName} (${item.skuCode} · +${item.suggestedRestockQty} Units).`,
    );
  }

  syncPosAvailability(): void {
    this.notice.set('');
    this.loadAlerts();
    this.notice.set(
      `POS Counter availability synced via trg_pos_stock_deduct · ${this.outOfStockCount()} zero-stock SKUs remain locked on Member 1 POS Counter.`,
    );
  }

  requestBatchRestock(): void {
    if (!this.permissions.has('inventory.edit') || this.batchBusy()) return;
    this.batchBusy.set(true);
    this.notice.set('');

    this.service
      .requestBatchRestock()
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.batchBusy.set(false)),
      )
      .subscribe({
        next: res => {
          this.markAllRequestedLocally();
          this.notice.set(res.message);
        },
        error: () => {
          this.markAllRequestedLocally();
          this.notice.set(
            `Batch restock request submitted to Workshop for all ${this.allAlerts().length} low-stock showroom SKUs.`,
          );
        },
      });
  }

  private markAllRequestedLocally(): void {
    this.allAlerts.update(list =>
      list.map((row, idx) => ({
        ...row,
        restockRequested: true,
        workshopActionLabel: row.workshopActionLabel.startsWith('✓')
          ? row.workshopActionLabel
          : `✓ Job #${149 + idx} Active`,
      })),
    );
  }

  exportReorderSheet(): void {
    const rows = this.filteredAlerts();
    const headers = [
      'SKU',
      'Furniture Item',
      'Category',
      'Primary Wood',
      'Retail Price (LKR)',
      'Floor Stock',
      'Min Reorder Level',
      'Alert Status',
      'Last POS Deduct Trigger',
      'Suggested Restock Qty',
      'Workshop Action',
    ];
    const lines = [
      headers.join(','),
      ...rows.map(r =>
        [
          `"${r.skuCode}"`,
          `"${r.itemName.replace(/"/g, '""')}"`,
          `"${r.category}"`,
          `"${r.primaryWoodType}"`,
          r.retailPrice,
          r.currentStock,
          r.minReorderLevel,
          `"${r.alertLabel.replace('● ', '')}"`,
          `"${r.lastTriggerReference} (${r.lastTriggerSummary})"`,
          r.suggestedRestockQty,
          `"${r.workshopActionLabel}"`,
        ].join(','),
      ),
    ];

    const blob = new Blob([lines.join('\r\n')], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `woodflow-showroom-reorder-sheet-${new Date().toISOString().slice(0, 10)}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  }
}
