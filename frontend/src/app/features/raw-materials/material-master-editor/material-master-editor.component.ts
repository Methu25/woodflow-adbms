import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { RawMaterialsService, SupplierItem } from '../services/raw-materials.service';

@Component({
  selector: 'app-material-master-editor',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './material-master-editor.component.html',
  styleUrls: ['../raw-materials-header.scss', './material-master-editor.component.scss']
})
export class MaterialMasterEditorComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly service = inject(RawMaterialsService);

  readonly categories = ['Timber', 'Boards', 'Hardware', 'Finishing', 'Fabric', 'Adhesives'];
  readonly units = ['cu.ft', 'BOARD_FEET', 'sheets', 'pcs', 'liters', 'GALLONS', 'meters', 'METERS', 'kg', 'BOXES'];

  readonly isEditMode = signal<boolean>(false);
  readonly materialId = signal<number | null>(null);
  readonly suppliers = signal<SupplierItem[]>([]);

  readonly materialCode = signal<string>('');
  readonly name = signal<string>('');
  readonly category = signal<string>('Timber');
  readonly unitOfMeasure = signal<string>('cu.ft');
  readonly quantityOnHand = signal<number>(0);
  readonly reorderLevel = signal<number>(10);
  readonly unitCost = signal<number>(0);
  readonly storageLocation = signal<string>('Main Yard · Bay A');
  readonly notes = signal<string>('');
  readonly preferredSupplierId = signal<number | null>(null);
  readonly leadTimeDays = signal<number>(3);
  readonly isActive = signal<boolean>(true);

  readonly submitting = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  readonly simulatedStatus = computed(() => {
    const onHand = this.quantityOnHand();
    const reorder = this.reorderLevel();
    if (onHand <= reorder * 0.5) return 'CRITICAL';
    if (onHand <= reorder) return 'LOW STOCK';
    return 'HEALTHY';
  });

  ngOnInit(): void {
    this.service.listSuppliers().subscribe({
      next: (list) => this.suppliers.set(list),
      error: () => {}
    });

    const idParam = this.route.snapshot.paramMap.get('id');
    const parsedId = idParam ? Number(idParam) : NaN;
    if (Number.isInteger(parsedId) && parsedId > 0) {
      this.isEditMode.set(true);
      this.materialId.set(parsedId);
      this.loadMaterial(parsedId);
    }
  }

  private loadMaterial(id: number): void {
    this.service.getMaterialById(id).subscribe({
      next: (item) => {
        this.materialCode.set(item.materialCode);
        this.name.set(item.name);
        this.category.set(item.category);
        this.unitOfMeasure.set(item.unitOfMeasure);
        this.quantityOnHand.set(item.quantityOnHand);
        this.reorderLevel.set(item.reorderLevel);
        this.unitCost.set(item.unitCost);
        this.storageLocation.set(item.storageLocation);
        this.notes.set(item.notes || '');
        this.isActive.set(item.isActive);
      },
      error: (err) => {
        this.errorMessage.set(err?.error?.message || 'Failed to load material.');
      }
    });
  }

  onCodeChange(e: Event): void {
    this.materialCode.set((e.target as HTMLInputElement).value.toUpperCase());
  }

  onNameChange(e: Event): void {
    this.name.set((e.target as HTMLInputElement).value);
  }

  onCategoryChange(e: Event): void {
    this.category.set((e.target as HTMLSelectElement).value);
  }

  onUnitChange(e: Event): void {
    this.unitOfMeasure.set((e.target as HTMLSelectElement).value);
  }

  onOnHandChange(e: Event): void {
    const v = parseFloat((e.target as HTMLInputElement).value) || 0;
    this.quantityOnHand.set(Math.max(0, v));
  }

  onReorderChange(e: Event): void {
    const v = parseFloat((e.target as HTMLInputElement).value) || 0;
    this.reorderLevel.set(Math.max(0, v));
  }

  onUnitCostChange(e: Event): void {
    const v = parseFloat((e.target as HTMLInputElement).value) || 0;
    this.unitCost.set(Math.max(0, v));
  }

  onStorageChange(e: Event): void {
    this.storageLocation.set((e.target as HTMLInputElement).value);
  }

  onNotesChange(e: Event): void {
    this.notes.set((e.target as HTMLTextAreaElement).value);
  }

  onSupplierChange(e: Event): void {
    const raw = (e.target as HTMLSelectElement).value;
    const parsed = raw ? Number(raw) : NaN;
    this.preferredSupplierId.set(Number.isInteger(parsed) && parsed > 0 ? parsed : null);
  }

  onLeadTimeChange(e: Event): void {
    const v = parseInt((e.target as HTMLInputElement).value, 10) || 3;
    this.leadTimeDays.set(Math.max(1, v));
  }

  onActiveChange(e: Event): void {
    this.isActive.set((e.target as HTMLInputElement).checked);
  }

  onSubmit(): void {
    this.errorMessage.set(null);

    if (!this.name().trim()) {
      this.errorMessage.set('Material name is required.');
      return;
    }

    this.submitting.set(true);

    if (this.isEditMode()) {
      const id = this.materialId();
      if (!id) return;

      this.service.updateMaterial(id, {
        name: this.name().trim(),
        category: this.category(),
        unitOfMeasure: this.unitOfMeasure(),
        reorderLevel: this.reorderLevel(),
        unitCost: this.unitCost(),
        storageLocation: this.storageLocation().trim(),
        notes: this.notes().trim(),
        isActive: this.isActive()
      }).subscribe({
        next: () => {
          this.submitting.set(false);
          void this.router.navigate(['/inventory/raw-materials']);
        },
        error: (err) => {
          this.errorMessage.set(err?.error?.message || 'Failed to update material.');
          this.submitting.set(false);
        }
      });
    } else {
      if (!this.materialCode().trim()) {
        this.errorMessage.set('Material code is required.');
        this.submitting.set(false);
        return;
      }

      this.service.createMaterial({
        materialCode: this.materialCode().trim(),
        name: this.name().trim(),
        category: this.category(),
        unitOfMeasure: this.unitOfMeasure(),
        quantityOnHand: this.quantityOnHand(),
        reorderLevel: this.reorderLevel(),
        unitCost: this.unitCost(),
        storageLocation: this.storageLocation().trim(),
        notes: this.notes().trim(),
        preferredSupplierId: this.preferredSupplierId() ?? undefined,
        leadTimeDays: this.leadTimeDays()
      }).subscribe({
        next: () => {
          this.submitting.set(false);
          void this.router.navigate(['/inventory/raw-materials']);
        },
        error: (err) => {
          this.errorMessage.set(err?.error?.message || 'Failed to create material SKU.');
          this.submitting.set(false);
        }
      });
    }
  }
}
