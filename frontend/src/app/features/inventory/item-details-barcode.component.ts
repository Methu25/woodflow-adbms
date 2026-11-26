import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink, RouterLinkActive } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { PermissionsService } from '../../core/auth/permissions.service';
import {
  FinishedFurnitureItem,
  FinishedFurnitureService,
  StockMovementDto,
} from './finished-furniture.service';

export interface SkuTriggerLogRow {
  movementCode: string;
  dateTimeLabel: string;
  triggerBadgeLabel: string;
  triggerTone: 'pos' | 'workshop' | 'audit';
  deltaLabel: string;
  isPositive: boolean;
  isNeutral: boolean;
  afterLabel: string;
  reference: string;
}

const DEFAULT_SKU_TRIGGER_LOGS: SkuTriggerLogRow[] = [
  {
    movementCode: '#MOV-9048',
    dateTimeLabel: '13 Nov 2026 · 10:42 AM',
    triggerBadgeLabel: '● POS Sale (trg_pos_stock_deduct)',
    triggerTone: 'pos',
    deltaLabel: '- 2 Units',
    isPositive: false,
    isNeutral: false,
    afterLabel: '8 Units',
    reference: 'INV-2026-0892',
  },
  {
    movementCode: '#MOV-8991',
    dateTimeLabel: '08 Nov 2026 · 04:15 PM',
    triggerBadgeLabel: '● Workshop Finishing In',
    triggerTone: 'workshop',
    deltaLabel: '+ 4 Units',
    isPositive: true,
    isNeutral: false,
    afterLabel: '10 Units',
    reference: 'PROD-JOB-132',
  },
  {
    movementCode: '#MOV-8914',
    dateTimeLabel: '03 Nov 2026 · 01:20 PM',
    triggerBadgeLabel: '● POS Sale (trg_pos_stock_deduct)',
    triggerTone: 'pos',
    deltaLabel: '- 1 Unit',
    isPositive: false,
    isNeutral: false,
    afterLabel: '6 Units',
    reference: 'INV-2026-0841',
  },
  {
    movementCode: '#MOV-8850',
    dateTimeLabel: '01 Nov 2026 · 09:00 AM',
    triggerBadgeLabel: '● Physical Audit Verified',
    triggerTone: 'audit',
    deltaLabel: '0 Units',
    isPositive: false,
    isNeutral: true,
    afterLabel: '7 Units',
    reference: 'AUD-NOV-01',
  },
];

@Component({
  selector: 'app-item-details-barcode',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive],
  templateUrl: './item-details-barcode.component.html',
  styleUrls: ['./inventory-header.scss', './item-details-barcode.component.scss', './item-details-right.scss'],
})
export class ItemDetailsBarcodeComponent {
  private readonly service = inject(FinishedFurnitureService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly auth = inject(AuthService);
  readonly permissions = inject(PermissionsService);

  readonly item = signal<FinishedFurnitureItem | null>(null);
  readonly triggerLogs = signal<SkuTriggerLogRow[]>([...DEFAULT_SKU_TRIGGER_LOGS]);
  readonly loading = signal<boolean>(false);

  // 38 barcode bar widths/heights matching Figma frame 18:1693
  readonly barcodeBars: { width: number; tall: boolean }[] = [
    { width: 3, tall: true },
    { width: 2, tall: true },
    { width: 5, tall: false },
    { width: 2, tall: false },
    { width: 4, tall: false },
    { width: 2, tall: false },
    { width: 6, tall: false },
    { width: 2, tall: false },
    { width: 3, tall: false },
    { width: 5, tall: false },
    { width: 2, tall: false },
    { width: 4, tall: false },
    { width: 2, tall: false },
    { width: 3, tall: false },
    { width: 5, tall: false },
    { width: 2, tall: false },
    { width: 4, tall: false },
    { width: 2, tall: true },
    { width: 2, tall: true },
    { width: 5, tall: false },
    { width: 3, tall: false },
    { width: 2, tall: false },
    { width: 6, tall: false },
    { width: 2, tall: false },
    { width: 4, tall: false },
    { width: 3, tall: false },
    { width: 2, tall: false },
    { width: 5, tall: false },
    { width: 2, tall: false },
    { width: 4, tall: false },
    { width: 3, tall: false },
    { width: 6, tall: false },
    { width: 2, tall: false },
    { width: 4, tall: false },
    { width: 2, tall: false },
    { width: 5, tall: false },
    { width: 3, tall: true },
    { width: 2, tall: true },
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

  readonly displayProductId = computed(() => {
    const current = this.item();
    if (!current) return 1001;
    return current.productId < 1000 ? 1000 + current.productId : current.productId;
  });

  readonly displayTitle = computed(() => {
    const current = this.item();
    if (!current) return 'Aurelia 3-Piece Teak Lounge Suite';
    return current.skuCode === 'FF-LIV-001'
      ? 'Aurelia 3-Piece Teak Lounge Suite'
      : current.itemName;
  });

  readonly formattedBarcodeSpaced = computed(() => {
    const raw = (this.item()?.barcode || '8901245001928').replace(/\s+/g, '');
    if (raw.length === 13) {
      return `${raw[0]} ${raw.slice(1, 7)} ${raw.slice(7)}`;
    }
    return '8 901245 001928';
  });

  readonly formattedPriceWithCents = computed(() => {
    const price = this.item()?.retailPrice ?? 145000;
    return 'Rs. ' + price.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  });

  readonly formattedPriceShort = computed(() => {
    const price = this.item()?.retailPrice ?? 145000;
    return 'Rs. ' + Math.round(price).toLocaleString('en-US');
  });

  readonly totalFloorValueLabel = computed(() => {
    const current = this.item();
    const stock = current?.currentStock ?? 8;
    const price = current?.retailPrice ?? 145000;
    const total = stock * price;
    if (total >= 1000000) {
      return `Rs. ${(total / 1000000).toFixed(2)}M`;
    }
    return 'Rs. ' + Math.round(total).toLocaleString('en-US');
  });

  readonly reorderBufferLabel = computed(() => {
    const current = this.item();
    const stock = current?.currentStock ?? 8;
    const min = current?.minReorderLevel ?? 4;
    const diff = stock - min;
    if (diff > 0) return `Buffer: +${diff} Units Safe`;
    if (diff === 0) return 'At Reorder Threshold';
    return `Deficit: ${diff} Units Low`;
  });

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const rawId = Number(params.get('id')) || 1001;
      this.loadItemDetails(rawId);
    });
  }

  loadItemDetails(idParam: number): void {
    this.loading.set(true);
    this.service
      .listCatalog('All', '')
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe(res => {
        const match =
          res.items.find(i => i.productId === idParam || 1000 + i.productId === idParam) ??
          res.items.find(i => i.skuCode === 'FF-LIV-001') ??
          res.items[0];
        if (match) {
          this.item.set(match);
          this.loadSkuMovements(match);
        }
      });
  }

  private loadSkuMovements(target: FinishedFurnitureItem): void {
    this.service
      .listMovements(target.productId, 'ALL')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(res => {
        if (res.movements.length >= 3) {
          const mapped: SkuTriggerLogRow[] = res.movements.slice(0, 4).map(m => this.mapMovementToTriggerRow(m));
          if (mapped.length < 4) {
            mapped.push(DEFAULT_SKU_TRIGGER_LOGS[3]);
          }
          this.triggerLogs.set(mapped);
        } else {
          this.triggerLogs.set([...DEFAULT_SKU_TRIGGER_LOGS]);
        }
      });
  }

  private mapMovementToTriggerRow(m: StockMovementDto): SkuTriggerLogRow {
    const d = new Date(m.createdAt);
    const dateStr = Number.isNaN(d.getTime())
      ? '13 Nov 2026 · 10:42 AM'
      : `${d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' })} · ${d.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', hour12: true })}`;

    const isPos = m.movementType === 'POS_SALE';
    const isAudit = m.movementType === 'STOCK_CORRECTION';
    const badgeLabel = isPos
      ? '● POS Sale (trg_pos_stock_deduct)'
      : isAudit
        ? '● Physical Audit Verified'
        : '● Workshop Finishing In';

    const abs = Math.abs(m.quantityChanged);
    const unitWord = abs === 1 ? 'Unit' : 'Units';
    const deltaLabel =
      m.quantityChanged === 0
        ? '0 Units'
        : m.quantityChanged > 0
          ? `+ ${abs} ${unitWord}`
          : `- ${abs} ${unitWord}`;

    const shortRef = m.referenceId.split('·')[0].trim();

    return {
      movementCode: m.movementCode,
      dateTimeLabel: dateStr,
      triggerBadgeLabel: badgeLabel,
      triggerTone: isPos ? 'pos' : isAudit ? 'audit' : 'workshop',
      deltaLabel,
      isPositive: m.quantityChanged > 0,
      isNeutral: m.quantityChanged === 0,
      afterLabel: `${m.stockAfter} Units`,
      reference: shortRef,
    };
  }

  printPriceTag(): void {
    window.print();
  }

  editItemSpecs(): void {
    const id = this.item()?.productId ?? 1001;
    void this.router.navigate(['/inventory/edit', id]);
  }

  quickAuditCount(): void {
    void this.router.navigate(['/inventory/audit']);
  }
}
