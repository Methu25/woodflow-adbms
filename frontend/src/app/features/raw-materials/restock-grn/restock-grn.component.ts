import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, finalize, forkJoin, of } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { PermissionsService } from '../../../core/auth/permissions.service';
import {
  GoodsReceiptRecord,
  RawMaterialItem,
  RawMaterialsService,
  SupplierItem,
} from '../services/raw-materials.service';

type StockStatus = 'CRITICAL' | 'LOW STOCK' | 'HEALTHY';

@Component({
  selector: 'app-restock-grn',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './restock-grn.component.html',
  styleUrls: ['../raw-materials-header.scss', './restock-grn.component.scss'],
})
export class RestockGrnComponent implements OnInit {
  private readonly service = inject(RawMaterialsService);
  private readonly auth = inject(AuthService);
  private readonly permissions = inject(PermissionsService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  readonly materials = signal<RawMaterialItem[]>([]);
  readonly suppliers = signal<SupplierItem[]>([]);
  readonly receipts = signal<GoodsReceiptRecord[]>([]);

  readonly loading = signal<boolean>(false);
  readonly submitting = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);
  readonly successBanner = signal<GoodsReceiptRecord | null>(null);
  readonly ledgerSearch = signal<string>('');

  readonly selectedMaterialId = signal<number | null>(null);
  readonly selectedSupplierId = signal<number | null>(null);
  readonly quantityReceived = signal<number>(10);
  readonly unitCost = signal<number>(0);
  readonly moisturePct = signal<number>(12);
  readonly receivedBy = signal<string>('Workshop Foreman');
  readonly notes = signal<string>('');

  readonly canCreate = computed(() => this.permissions.has('inventory.create'));

  readonly selectedMaterial = computed<RawMaterialItem | null>(() => {
    const id = this.selectedMaterialId();
    if (!id) return null;
    return this.materials().find(m => m.materialId === id) ?? null;
  });

  readonly selectedSupplier = computed<SupplierItem | null>(() => {
    const id = this.selectedSupplierId();
    if (!id) return null;
    return this.suppliers().find(s => s.supplierId === id) ?? null;
  });

  readonly projectedOnHand = computed<number>(() => {
    const mat = this.selectedMaterial();
    if (!mat) return 0;
    return mat.quantityOnHand + Math.max(0, this.quantityReceived());
  });

  readonly projectedStatus = computed<StockStatus>(() => {
    const mat = this.selectedMaterial();
    if (!mat) return 'HEALTHY';
    return this.computeStatus(this.projectedOnHand(), mat.reorderLevel);
  });

  readonly clearsLowStockAlert = computed<boolean>(() => {
    const mat = this.selectedMaterial();
    if (!mat) return false;
    return mat.stockStatus !== 'HEALTHY' && this.projectedStatus() === 'HEALTHY';
  });

  readonly lineTotalLkr = computed<number>(() => {
    return Math.max(0, this.quantityReceived()) * Math.max(0, this.unitCost());
  });

  readonly projectedValuationLkr = computed<number>(() => {
    return this.projectedOnHand() * Math.max(0, this.unitCost());
  });

  readonly moistureAssessment = computed<{ level: 'IDEAL' | 'CAUTION' | 'HIGH' | 'STANDARD'; label: string }>(() => {
    const mat = this.selectedMaterial();
    if (!mat || (mat.category !== 'Timber' && mat.category !== 'Boards')) {
      return { level: 'STANDARD', label: 'N/A · Non-Timber' };
    }
    const pct = this.moisturePct();
    if (pct <= 14) return { level: 'IDEAL', label: 'Kiln-Dried OK' };
    if (pct <= 18) return { level: 'CAUTION', label: 'Air-Dried / Monitor' };
    return { level: 'HIGH', label: 'High Moisture Risk' };
  });

  readonly totalReceiptsCount = computed<number>(() => this.receipts().length);

  readonly totalUnitsReceived = computed<number>(() =>
    this.receipts().reduce((sum, r) => sum + r.quantityReceived, 0),
  );

  readonly totalGrnValueLkr = computed<number>(() =>
    this.receipts().reduce((sum, r) => sum + (r.quantityReceived * r.unitCost), 0),
  );

  readonly lowStockSkuCount = computed<number>(() =>
    this.materials().filter(m => m.stockStatus !== 'HEALTHY').length,
  );

  readonly filteredReceipts = computed<GoodsReceiptRecord[]>(() => {
    const term = this.ledgerSearch().trim().toLowerCase();
    const list = this.receipts();
    if (!term) return list;
    return list.filter(r =>
      r.grnNumber.toLowerCase().includes(term) ||
      r.materialCode.toLowerCase().includes(term) ||
      r.materialName.toLowerCase().includes(term) ||
      (r.supplierName ?? '').toLowerCase().includes(term) ||
      r.receivedBy.toLowerCase().includes(term) ||
      (r.notes ?? '').toLowerCase().includes(term),
    );
  });

  ngOnInit(): void {
    this.permissions.refresh().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => {} });

    const userFullName = this.auth.user()?.fullName?.trim();
    if (userFullName) {
      this.receivedBy.set(userFullName);
    }

    this.loadInitialData();
  }

  refreshData(): void {
    this.errorMessage.set(null);
    this.loadData(false);
  }

  onMaterialChange(event: Event): void {
    const raw = (event.target as HTMLSelectElement).value;
    const parsed = raw ? Number(raw) : NaN;
    if (!Number.isInteger(parsed) || parsed <= 0) {
      this.selectedMaterialId.set(null);
      return;
    }
    this.applyMaterialSelection(parsed, null);
  }

  onSupplierChange(event: Event): void {
    const raw = (event.target as HTMLSelectElement).value;
    const parsed = raw ? Number(raw) : NaN;
    const supplierId = Number.isInteger(parsed) && parsed > 0 ? parsed : null;
    this.selectedSupplierId.set(supplierId);

    const matId = this.selectedMaterialId();
    if (supplierId && matId) {
      const supplier = this.suppliers().find(s => s.supplierId === supplierId);
      const contract = supplier?.contractedMaterials?.find(cm => cm.materialId === matId);
      if (contract && contract.unitPrice > 0) {
        this.unitCost.set(contract.unitPrice);
      }
    }
  }

  onQtyChange(event: Event): void {
    const v = parseFloat((event.target as HTMLInputElement).value);
    this.quantityReceived.set(Number.isFinite(v) ? Math.max(0, v) : 0);
  }

  onUnitCostChange(event: Event): void {
    const v = parseFloat((event.target as HTMLInputElement).value);
    this.unitCost.set(Number.isFinite(v) ? Math.max(0, v) : 0);
  }

  onMoistureChange(event: Event): void {
    const v = parseFloat((event.target as HTMLInputElement).value);
    this.moisturePct.set(Number.isFinite(v) ? Math.min(100, Math.max(0, v)) : 0);
  }

  onReceivedByChange(event: Event): void {
    this.receivedBy.set((event.target as HTMLInputElement).value);
  }

  onNotesChange(event: Event): void {
    this.notes.set((event.target as HTMLTextAreaElement).value);
  }

  onLedgerSearch(event: Event): void {
    this.ledgerSearch.set((event.target as HTMLInputElement).value);
  }

  resetForm(): void {
    this.errorMessage.set(null);
    this.successBanner.set(null);
    this.notes.set('');
    this.quantityReceived.set(10);

    const userFullName = this.auth.user()?.fullName?.trim();
    this.receivedBy.set(userFullName || 'Workshop Foreman');

    const matId = this.selectedMaterialId();
    if (matId) {
      this.applyMaterialSelection(matId, null);
    }
  }

  onSubmit(event: Event): void {
    event.preventDefault();
    this.errorMessage.set(null);
    this.successBanner.set(null);

    if (!this.canCreate()) {
      this.errorMessage.set('You do not have inventory.create permission to post Goods Receipt Notes.');
      return;
    }

    const materialId = this.selectedMaterialId();
    if (!materialId) {
      this.errorMessage.set('Please select a raw material SKU to restock.');
      return;
    }

    const qty = this.quantityReceived();
    if (!Number.isFinite(qty) || qty <= 0) {
      this.errorMessage.set('Quantity received must be greater than zero.');
      return;
    }

    const cost = this.unitCost();
    if (!Number.isFinite(cost) || cost < 0) {
      this.errorMessage.set('Unit cost cannot be negative.');
      return;
    }

    const moisture = this.moisturePct();
    if (!Number.isFinite(moisture) || moisture < 0 || moisture > 100) {
      this.errorMessage.set('Moisture percentage must be between 0% and 100%.');
      return;
    }

    const receiver = this.receivedBy().trim();
    if (!receiver) {
      this.errorMessage.set('Received By (Foreman / Storekeeper name) is required.');
      return;
    }

    this.submitting.set(true);

    this.service
      .postGoodsReceipt({
        materialId,
        supplierId: this.selectedSupplierId() ?? undefined,
        quantityReceived: qty,
        unitCost: cost,
        moisturePct: moisture,
        receivedBy: receiver,
        notes: this.notes().trim() || undefined,
      })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.submitting.set(false)),
      )
      .subscribe({
        next: (created) => {
          this.successBanner.set(created);
          this.notes.set('');
          this.loadData(false);
        },
        error: (err: unknown) => {
          this.errorMessage.set(this.extractErrorMessage(err, 'Failed to post Goods Receipt Note.'));
        },
      });
  }

  private loadInitialData(): void {
    this.loadData(true);
  }

  private loadData(applyQuerySelection: boolean): void {
    this.loading.set(true);

    forkJoin({
      materials: this.service.listCatalog().pipe(
        catchError((err: unknown) => {
          this.errorMessage.set(this.extractErrorMessage(err, 'Failed to load raw material catalog.'));
          return of([] as RawMaterialItem[]);
        }),
      ),
      suppliers: this.service.listSuppliers().pipe(
        catchError(() => of([] as SupplierItem[])),
      ),
      receipts: this.service.listGoodsReceipts(50).pipe(
        catchError((err: unknown) => {
          if (!this.errorMessage()) {
            this.errorMessage.set(this.extractErrorMessage(err, 'Failed to load Goods Receipt ledger.'));
          }
          return of([] as GoodsReceiptRecord[]);
        }),
      ),
    })
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe(({ materials, suppliers, receipts }) => {
        this.materials.set(materials);
        this.suppliers.set(suppliers);
        this.receipts.set(receipts);

        if (applyQuerySelection && materials.length > 0) {
          this.applyInitialQueryDefaults(materials);
        } else if (this.selectedMaterialId() && materials.length > 0) {
          const stillExists = materials.some(m => m.materialId === this.selectedMaterialId());
          if (!stillExists) {
            this.applyInitialQueryDefaults(materials);
          }
        }
      });
  }

  private applyInitialQueryDefaults(materials: RawMaterialItem[]): void {
    const qp = this.route.snapshot.queryParamMap;
    const rawMatId = qp.get('materialId');
    const parsedMatId = rawMatId ? Number(rawMatId) : NaN;

    const rawQty = qp.get('qty');
    const parsedQty = rawQty ? parseFloat(rawQty) : NaN;
    const initialQty = Number.isFinite(parsedQty) && parsedQty > 0 ? parsedQty : null;

    if (Number.isInteger(parsedMatId) && parsedMatId > 0 && materials.some(m => m.materialId === parsedMatId)) {
      this.applyMaterialSelection(parsedMatId, initialQty);
      return;
    }

    const firstLow = materials.find(m => m.stockStatus !== 'HEALTHY') ?? materials[0];
    if (firstLow) {
      this.applyMaterialSelection(firstLow.materialId, initialQty);
    }
  }

  private applyMaterialSelection(materialId: number, overrideQty: number | null): void {
    const mat = this.materials().find(m => m.materialId === materialId);
    if (!mat) return;

    this.selectedMaterialId.set(mat.materialId);
    this.unitCost.set(mat.unitCost);

    if (overrideQty !== null) {
      this.quantityReceived.set(overrideQty);
    }

    const isWood = mat.category === 'Timber' || mat.category === 'Boards';
    this.moisturePct.set(isWood ? 12 : 0);

    const preferredSupplier = this.findSupplierForMaterial(mat.materialId);
    this.selectedSupplierId.set(preferredSupplier?.supplierId ?? null);
  }

  private findSupplierForMaterial(materialId: number): SupplierItem | null {
    const list = this.suppliers();
    const preferred = list.find(s =>
      s.contractedMaterials?.some(cm => cm.materialId === materialId && cm.isPreferred),
    );
    if (preferred) return preferred;

    const anyMatch = list.find(s =>
      s.contractedMaterials?.some(cm => cm.materialId === materialId),
    );
    return anyMatch ?? null;
  }

  private computeStatus(onHand: number, reorder: number): StockStatus {
    if (onHand <= reorder * 0.5) return 'CRITICAL';
    if (onHand <= reorder) return 'LOW STOCK';
    return 'HEALTHY';
  }

  private extractErrorMessage(err: unknown, fallback: string): string {
    if (err instanceof HttpErrorResponse) {
      if (err.status === 403) {
        return 'Access denied: you lack the required inventory permission for this action.';
      }
      if (err.status === 503) {
        return 'Backend service or PostgreSQL database is temporarily unavailable.';
      }
      if (err.error && typeof err.error === 'object' && 'message' in err.error && typeof err.error.message === 'string') {
        return err.error.message;
      }
    }
    return fallback;
  }
}
