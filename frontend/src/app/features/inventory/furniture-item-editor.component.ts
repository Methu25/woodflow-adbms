import { CommonModule } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink, RouterLinkActive } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { PermissionsService } from '../../core/auth/permissions.service';
import {
  FinishedFurnitureService,
  UpsertFinishedFurnitureRequest,
} from './finished-furniture.service';

export interface GalleryThumbnailOption {
  key: string;
  label: string;
  subtitle: string;
}

@Component({
  selector: 'app-furniture-item-editor',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink, RouterLinkActive],
  templateUrl: './furniture-item-editor.component.html',
  styleUrls: ['./inventory-header.scss', './furniture-item-editor.component.scss', './furniture-item-form.scss'],
})
export class FurnitureItemEditorComponent {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(FinishedFurnitureService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly auth = inject(AuthService);
  readonly permissions = inject(PermissionsService);

  readonly editId = signal<number | null>(null);
  readonly isEditMode = computed(() => this.editId() !== null);
  readonly loading = signal<boolean>(false);
  readonly saving = signal<boolean>(false);
  readonly error = signal<string>('');
  readonly success = signal<string>('');

  readonly selectedImageKey = signal<string>('lounge');
  readonly uploadedDataUrl = signal<string | null>(null);
  readonly uploadedFileName = signal<string>('');

  readonly categories: string[] = [
    'Living Room',
    'Bedroom',
    'Dining Room',
    'Office',
    'Kids & Nursery',
    'Verandah & Outdoor',
  ];

  readonly woodTypes: string[] = [
    'Seasoned Ceylon Teak (Grade A)',
    'Solid Teak',
    'Burma Teak',
    'Polished Mahogany',
    'Red Mahogany',
    'Nadun & Teak',
    'Solid Nadun',
    'Cushioned Teak',
  ];

  readonly galleryThumbnails: GalleryThumbnailOption[] = [
    { key: 'lounge', label: 'Lounge Front View', subtitle: 'Teak 3-Seater Suite' },
    { key: 'bed', label: 'Bedroom Suite View', subtitle: 'Royal Carved Frame' },
    { key: 'dining', label: 'Dining & Wood Detail', subtitle: 'Burma Teak Grain' },
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

  readonly form = this.fb.nonNullable.group({
    skuCode: ['FF-LIV-001', [Validators.required, Validators.maxLength(30)]],
    barcode: ['8901245001928', [Validators.required, Validators.minLength(8), Validators.maxLength(30)]],
    itemName: ['Aurelia 3-Piece Teak Lounge Suite (3+1+1)', [Validators.required, Validators.maxLength(100)]],
    category: ['Living Room', [Validators.required]],
    primaryWoodType: ['Seasoned Ceylon Teak (Grade A)', [Validators.required, Validators.maxLength(80)]],
    retailPrice: [145000, [Validators.required, Validators.min(0)]],
    minReorderLevel: [4, [Validators.required, Validators.min(0)]],
    currentStock: [8, [Validators.required, Validators.min(0)]],
    dimensionsFinish: [
      '84" × 32" × 30" (3-Seater) · Matte PU Clear Lacquer + 32D Foam Cushions',
      [Validators.required, Validators.maxLength(180)],
    ],
    descriptionNotes: [
      'Hand-crafted solid Burma & Ceylon teak lounge suite with removable linen upholstery.\nIncludes 10-year structural joinery warranty. Standard delivery time: 2–3 days.',
      [Validators.required, Validators.maxLength(1000)],
    ],
    showroomZone: ['Ground Floor · Bay A2'],
  });

  readonly userDisplayName = computed(() => this.auth.user()?.fullName || 'P Rejishanth');
  readonly userRoleSubtitle = computed(() =>
    this.auth.user()?.role === 'OWNER' ? 'Owner · Finished Stock Lead' : 'Finished Stock Lead',
  );
  readonly userInitials = computed(() => {
    const parts = this.userDisplayName().trim().split(/\s+/);
    return parts.slice(0, 2).map(p => p[0]?.toUpperCase() ?? '').join('') || 'PR';
  });

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(params => {
      const rawId = params.get('id');
      if (rawId && !Number.isNaN(Number(rawId))) {
        const id = Number(rawId);
        this.editId.set(id);
        this.loadItemForEdit(id);
      } else {
        this.editId.set(null);
      }
    });
  }

  loadItemForEdit(productId: number): void {
    this.loading.set(true);
    this.error.set('');
    this.service
      .getById(productId)
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: item => {
          const woodMatch = this.woodTypes.includes(item.primaryWoodType)
            ? item.primaryWoodType
            : item.primaryWoodType || 'Seasoned Ceylon Teak (Grade A)';
          if (!this.woodTypes.includes(woodMatch)) {
            this.woodTypes.unshift(woodMatch);
          }

          this.form.patchValue({
            skuCode: item.skuCode,
            barcode: item.barcode,
            itemName: item.itemName,
            category: item.category,
            primaryWoodType: woodMatch,
            retailPrice: item.retailPrice,
            minReorderLevel: item.minReorderLevel,
            currentStock: item.currentStock,
            dimensionsFinish: item.dimensionsFinish,
            descriptionNotes: item.descriptionNotes,
            showroomZone: item.showroomZone,
          });
          this.selectedImageKey.set(item.imageKey || 'lounge');
        },
        error: () => {
          this.error.set('Could not load the selected furniture item.');
        },
      });
  }

  selectThumbnail(key: string): void {
    this.uploadedDataUrl.set(null);
    this.selectedImageKey.set(key);
  }

  onPhotoSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    if (file.size > 5 * 1024 * 1024) {
      this.error.set('Photo exceeds the 5 MB limit. Choose a smaller JPG, PNG, or WebP file.');
      return;
    }
    this.uploadedFileName.set(file.name);
    const reader = new FileReader();
    reader.onload = () => {
      if (typeof reader.result === 'string') {
        this.uploadedDataUrl.set(reader.result);
      }
    };
    reader.readAsDataURL(file);
  }

  generateEan13(): void {
    const randomSuffix = Math.floor(1000 + Math.random() * 9000);
    this.form.controls.barcode.setValue(`890124500${randomSuffix}`);
  }

  previewTitle(): string {
    return this.form.controls.itemName.value.trim() || 'Aurelia 3-Piece Teak Lounge Suite';
  }

  previewWoodShort(): string {
    const wood = this.form.controls.primaryWoodType.value.trim();
    return wood.replace(/\s*\(Grade [A-Z]\)/i, '') || 'Seasoned Ceylon Teak';
  }

  previewPriceFormatted(): string {
    const val = Number(this.form.controls.retailPrice.value) || 0;
    return 'Rs. ' + val.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  }

  previewStockBadgeText(): string {
    const stock = Number(this.form.controls.currentStock.value) || 0;
    const min = Number(this.form.controls.minReorderLevel.value) || 0;
    if (stock <= 0) return 'Out of Stock';
    if (stock <= min) return `Low Stock (${stock})`;
    return `In Stock (${stock})`;
  }

  previewStockBadgeClass(): string {
    const stock = Number(this.form.controls.currentStock.value) || 0;
    const min = Number(this.form.controls.minReorderLevel.value) || 0;
    if (stock <= 0) return 'badge-out';
    if (stock <= min) return 'badge-low';
    return 'badge-in';
  }

  save(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.saving()) {
      this.error.set('Please complete all required furniture specifications marked with *.');
      return;
    }

    this.saving.set(true);
    this.error.set('');
    this.success.set('');

    const raw = this.form.getRawValue();
    const payload: UpsertFinishedFurnitureRequest = {
      skuCode: raw.skuCode.trim().toUpperCase(),
      barcode: raw.barcode.trim(),
      itemName: raw.itemName.trim(),
      category: raw.category,
      primaryWoodType: raw.primaryWoodType.trim(),
      retailPrice: Number(raw.retailPrice),
      minReorderLevel: Number(raw.minReorderLevel),
      currentStock: Number(raw.currentStock),
      dimensionsFinish: raw.dimensionsFinish.trim(),
      descriptionNotes: raw.descriptionNotes.trim(),
      showroomZone: raw.showroomZone,
      imageKey: this.selectedImageKey(),
      isActive: true,
    };

    const id = this.editId();
    const request$ =
      id !== null ? this.service.updateItem(id, payload) : this.service.createItem(payload);

    request$
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.saving.set(false)),
      )
      .subscribe({
        next: saved => {
          this.success.set(
            id !== null
              ? `Updated ${saved.itemName} (${saved.skuCode}) in Showroom Catalog.`
              : `Added ${saved.itemName} (${saved.skuCode}) to Showroom Catalog.`,
          );
          setTimeout(() => {
            void this.router.navigate(['/inventory']);
          }, 800);
        },
        error: (err: unknown) => {
          if (err instanceof HttpErrorResponse && err.error?.message) {
            this.error.set(err.error.message);
          } else {
            this.error.set('Unable to save item specifications. Verify your permissions and SKU uniqueness.');
          }
        },
      });
  }

  cancel(): void {
    void this.router.navigate(['/inventory']);
  }
}
