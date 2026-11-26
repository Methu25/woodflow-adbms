import { CommonModule } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { PermissionsService } from '../../core/auth/permissions.service';
import {
  CategorySummaryCount,
  FinishedFurnitureItem,
  FinishedFurnitureService,
} from './finished-furniture.service';

@Component({
  selector: 'app-showroom-catalog',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, RouterLinkActive],
  templateUrl: './showroom-catalog.component.html',
  styleUrls: ['./inventory-header.scss', './showroom-catalog.component.scss'],
})
export class ShowroomCatalogComponent {
  private readonly service = inject(FinishedFurnitureService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly router = inject(Router);
  readonly auth = inject(AuthService);
  readonly permissions = inject(PermissionsService);

  readonly items = signal<FinishedFurnitureItem[]>([]);
  readonly totalSkus = signal<number>(24);
  readonly categoryCounts = signal<CategorySummaryCount[]>([]);
  readonly selectedCategory = signal<string>('All');
  readonly searchQuery = signal<string>('');
  readonly globalSearchQuery = signal<string>('');
  readonly loading = signal<boolean>(false);
  readonly error = signal<string>('');

  readonly categories: string[] = ['All', 'Living Room', 'Bedroom', 'Dining Room', 'Office'];

  readonly navTabs = [
    { label: '01 Showroom Catalog', route: '/inventory', exact: true },
    { label: '02 Add / Edit Item', route: '/inventory/add', exact: false },
    { label: '03 Stock Movements', route: '/inventory/movements', exact: false },
    { label: '04 Stock Audit', route: '/inventory/audit', exact: false },
    { label: '05 Categories', route: '/inventory/categories', exact: false },
    { label: '06 Item Details & Tag', route: '/inventory/items/1001', exact: false },
    { label: '07 Low-Stock Alerts', route: '/inventory/alerts', exact: false },
    { label: '📦 Raw Materials (Member 6) →', route: '/inventory/raw-materials', exact: false },
  ];

  readonly userDisplayName = computed(() => this.auth.user()?.fullName || 'P Rejishanth');
  readonly userRoleSubtitle = computed(() =>
    this.auth.user()?.role === 'OWNER' ? 'Owner · Finished Stock Lead' : 'Finished Stock Lead',
  );
  readonly userInitials = computed(() => {
    const parts = this.userDisplayName().trim().split(/\s+/);
    return parts.slice(0, 2).map(p => p[0]?.toUpperCase() ?? '').join('') || 'PR';
  });

  readonly lowStockAlertCount = computed(() =>
    this.items().filter(i => i.currentStock <= i.minReorderLevel).length || 6,
  );

  constructor() {
    this.loadCatalog();
  }

  loadCatalog(): void {
    this.loading.set(true);
    this.error.set('');
    this.service
      .listCatalog(this.selectedCategory(), this.searchQuery())
      .pipe(
        takeUntilDestroyed(this.destroyRef),
        finalize(() => this.loading.set(false)),
      )
      .subscribe({
        next: res => {
          this.items.set(res.items);
          this.totalSkus.set(res.totalActiveSkus > 0 ? res.totalActiveSkus : 24);
          this.categoryCounts.set(res.categoryCounts);
        },
        error: () => {
          this.error.set('Unable to load showroom catalog from the server.');
        },
      });
  }

  selectCategory(category: string): void {
    if (this.selectedCategory() === category) return;
    this.selectedCategory.set(category);
    this.loadCatalog();
  }

  onFilterInput(value: string): void {
    this.searchQuery.set(value);
    this.loadCatalog();
  }

  onGlobalSearch(value: string): void {
    this.globalSearchQuery.set(value);
    this.searchQuery.set(value);
    this.loadCatalog();
  }

  formatPrice(price: number): string {
    return 'Rs. ' + Math.round(price).toLocaleString('en-US');
  }

  stockBadgeText(item: FinishedFurnitureItem): string {
    if (item.stockStatus === 'OUT_OF_STOCK' || item.currentStock <= 0) {
      return 'Out of Stock';
    }
    if (item.stockStatus === 'LOW_STOCK' || item.currentStock <= item.minReorderLevel) {
      return `Low Stock (${item.currentStock})`;
    }
    return `In Stock (${item.currentStock})`;
  }

  stockBadgeClass(item: FinishedFurnitureItem): string {
    if (item.stockStatus === 'OUT_OF_STOCK' || item.currentStock <= 0) {
      return 'badge-out';
    }
    if (item.stockStatus === 'LOW_STOCK' || item.currentStock <= item.minReorderLevel) {
      return 'badge-low';
    }
    return 'badge-in';
  }

  viewDetails(item: FinishedFurnitureItem): void {
    void this.router.navigate(['/inventory/items', item.productId]);
  }

  editItem(item: FinishedFurnitureItem): void {
    void this.router.navigate(['/inventory/edit', item.productId]);
  }

  addItem(): void {
    void this.router.navigate(['/inventory/add']);
  }
}
