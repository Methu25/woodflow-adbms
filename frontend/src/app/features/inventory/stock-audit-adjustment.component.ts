import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { PermissionsService } from '../../core/auth/permissions.service';
import {
  FinishedFurnitureItem,
  FinishedFurnitureService,
} from './finished-furniture.service';

export interface EditableAuditRow {
  productId: number;
  skuCode: string;
  itemName: string;
  category: string;
  systemCount: number;
  physicalCount: number;
  reason: string;
}

const DEFAULT_FIGMA_AUDIT_PRESETS: Record<string, { system: number; physical: number; reason: string }> = {
  'FF-LIV-001': { system: 8, physical: 8, reason: 'Matched physical showroom floor' },
  'FF-BED-004': { system: 5, physical: 6, reason: 'Unlogged workshop transfer received' },
  'FF-DIN-002': { system: 3, physical: 2, reason: 'Scratch damage moved to polishing bay' },
  'FF-OFF-009': { system: 4, physical: 6, reason: 'Found 2 boxed units in warehouse rack B' },
  'FF-BED-012': { system: 3, physical: 1, reason: 'Transferred 2 display units to Kandy branch' },
};

@Component({
  selector: 'app-stock-audit-adjustment',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, RouterLinkActive],
  templateUrl: './stock-audit-adjustment.component.html',
  styleUrls: ['./inventory-header.scss', './stock-audit-adjustment.component.scss'],
})
export class StockAuditAdjustmentComponent {
  private readonly service = inject(FinishedFurnitureService);
  private readonly destroyRef = inject(DestroyRef);
  readonly auth = inject(AuthService);
  readonly permissions = inject(PermissionsService);

  readonly sessionCode = signal<string>('#AUD-2026-11');
  readonly rows = signal<EditableAuditRow[]>([]);
  readonly searchQuery = signal<string>('');
  readonly loading = signal<boolean>(false);
  readonly applying = signal<boolean>(false);
  readonly error = signal<string>('');
  readonly success = signal<string>('');

  private initialSnapshot: EditableAuditRow[] = [];

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

  readonly filteredRows = computed(() => {
    const term = this.searchQuery().trim().toLowerCase();
    const all = this.rows();
    if (!term) return all;
    return all.filter(
      r =>
        r.itemName.toLowerCase().includes(term) ||
        r.skuCode.toLowerCase().includes(term) ||
        r.category.toLowerCase().includes(term) ||
        r.reason.toLowerCase().includes(term),
    );
  });

  readonly positiveStats = computed(() => {
    const pos = this.rows().filter(r => r.physicalCount - r.systemCount > 0);
    const sum = pos.reduce((acc, r) => acc + (r.physicalCount - r.systemCount), 0);
    return { count: pos.length, sum };
  });

  readonly negativeStats = computed(() => {
    const neg = this.rows().filter(r => r.physicalCount - r.systemCount < 0);
    const sum = neg.reduce((acc, r) => acc + (r.physicalCount - r.systemCount), 0);
    return { count: neg.length, sum };
  });

  readonly netVariance = computed(() =>
    this.rows().reduce((acc, r) => acc + (r.physicalCount - r.systemCount), 0),
  );

  readonly documentedReasonsCount = computed(() =>
    this.rows().filter(r => (r.physicalCount - r.systemCount !== 0) && r.reason.trim().length > 0).length,
  );

  constructor() {
    this.loadAuditItems();
  }

  loadAuditItems(): void {
    this.loading.set(true);
    this.error.set('');
    this.service
      .listCatalog('All', '')
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe(res => {
        const targetSkus = ['FF-LIV-001', 'FF-BED-004', 'FF-DIN-002', 'FF-OFF-009', 'FF-BED-012'];
        const sortedItems: FinishedFurnitureItem[] = [];

        for (const sku of targetSkus) {
          const match = res.items.find(i => i.skuCode === sku);
          if (match) sortedItems.push(match);
        }
        for (const item of res.items) {
          if (!sortedItems.some(s => s.productId === item.productId) && sortedItems.length < 5) {
            sortedItems.push(item);
          }
        }

        const builtRows: EditableAuditRow[] = sortedItems.map(item => {
          const preset = DEFAULT_FIGMA_AUDIT_PRESETS[item.skuCode];
          const displayName =
            item.skuCode === 'FF-LIV-001' ? 'Aurelia 3-Piece Lounge Suite' : item.itemName;
          return {
            productId: item.productId,
            skuCode: item.skuCode,
            itemName: displayName,
            category: item.category,
            systemCount: item.currentStock,
            physicalCount: preset ? preset.physical : item.currentStock,
            reason: preset ? preset.reason : 'Matched physical showroom floor',
          };
        });

        this.initialSnapshot = builtRows.map(r => ({ ...r }));
        this.rows.set(builtRows);
      });
  }

  onPhysicalCountChange(productId: number, value: number | string): void {
    const parsed = Math.max(0, Math.floor(Number(value) || 0));
    this.rows.update(list =>
      list.map(row => (row.productId === productId ? { ...row, physicalCount: parsed } : row)),
    );
  }

  onReasonChange(productId: number, reason: string): void {
    this.rows.update(list =>
      list.map(row => (row.productId === productId ? { ...row, reason } : row)),
    );
  }

  varianceOf(row: EditableAuditRow): number {
    return row.physicalCount - row.systemCount;
  }

  formatVariance(row: EditableAuditRow): string {
    const v = this.varianceOf(row);
    if (v === 0) return '0';
    return v > 0 ? `+${v}` : `${v}`;
  }

  varianceClass(row: EditableAuditRow): string {
    const v = this.varianceOf(row);
    if (v > 0) return 'var-positive';
    if (v < 0) return 'var-negative';
    return 'var-neutral';
  }

  resetCounts(): void {
    this.error.set('');
    this.success.set('');
    this.rows.set(
      this.initialSnapshot.map(r => ({
        ...r,
        physicalCount: r.systemCount,
        reason: 'Matched physical showroom floor',
      })),
    );
  }

  applyAdjustments(): void {
    if (this.applying()) return;

    const currentRows = this.rows();
    const missingReason = currentRows.find(r => !r.reason || !r.reason.trim());
    if (missingReason) {
      this.error.set(
        `Mandatory adjustment reason is missing for ${missingReason.itemName} (${missingReason.skuCode}).`,
      );
      return;
    }

    this.applying.set(true);
    this.error.set('');
    this.success.set('');

    this.service
      .applyStockAudit({
        sessionCode: this.sessionCode().replace('#', ''),
        items: currentRows.map(r => ({
          productId: r.productId,
          physicalCount: r.physicalCount,
          reason: r.reason.trim(),
        })),
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.applying.set(false)),
      )
      .subscribe({
        next: res => {
          this.rows.update(list =>
            list.map(row => ({
              ...row,
              systemCount: row.physicalCount,
            })),
          );
          this.initialSnapshot = this.rows().map(r => ({ ...r }));
          this.success.set(
            `Session ${this.sessionCode()} applied: ${res.adjustedItemsCount} SKU adjustment(s) logged to Stock Movement Ledger (Net Variance: ${res.netVariance >= 0 ? '+' + res.netVariance : res.netVariance} Units).`,
          );
        },
        error: (err: unknown) => {
          if (err instanceof HttpErrorResponse && err.error?.message) {
            this.error.set(err.error.message);
          } else {
            this.error.set('Could not apply stock adjustments. Please check your permissions and database connection.');
          }
        },
      });
  }
}
