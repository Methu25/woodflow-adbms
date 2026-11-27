import { CommonModule } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { RawMaterialsService, SupplierItem } from '../services/raw-materials.service';

@Component({
  selector: 'app-supplier-directory',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './supplier-directory.component.html',
  styleUrls: ['../raw-materials-header.scss', './supplier-directory.component.scss']
})
export class SupplierDirectoryComponent implements OnInit {
  private readonly service = inject(RawMaterialsService);

  readonly suppliers = signal<SupplierItem[]>([]);
  readonly loading = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);

  readonly categories = ['All', 'Timber', 'Hardware', 'Finishing', 'Boards', 'Fabric'];
  readonly selectedCategory = signal<string>('All');
  readonly searchQuery = signal<string>('');

  // KPIs
  readonly totalSuppliers = computed(() => this.suppliers().length);
  readonly activeSuppliersCount = computed(() => this.suppliers().filter(s => s.isActive).length);
  readonly avgLeadTime = computed(() => {
    const list = this.suppliers();
    if (!list.length) return '0.0';
    const sum = list.reduce((acc, s) => acc + (s.avgLeadTimeDays || 3), 0);
    return (sum / list.length).toFixed(1);
  });
  readonly totalContractedMaterials = computed(() => {
    return this.suppliers().reduce((acc, s) => acc + (s.materialsSuppliedCount || 0), 0);
  });

  // Filtered suppliers
  readonly filteredSuppliers = computed(() => {
    const cat = this.selectedCategory();
    const query = this.searchQuery().trim().toLowerCase();

    return this.suppliers().filter(s => {
      const matchCat = cat === 'All' || s.primaryCategory.toLowerCase() === cat.toLowerCase();
      const matchQuery = !query ||
        s.companyName.toLowerCase().includes(query) ||
        s.supplierCode.toLowerCase().includes(query) ||
        s.contactPerson.toLowerCase().includes(query);
      return matchCat && matchQuery;
    });
  });

  // Register Modal state
  readonly showRegisterModal = signal<boolean>(false);
  readonly submittingModal = signal<boolean>(false);
  readonly modalError = signal<string | null>(null);

  readonly newSupplierCode = signal<string>('');
  readonly newCompanyName = signal<string>('');
  readonly newContactPerson = signal<string>('');
  readonly newPhone = signal<string>('');
  readonly newEmail = signal<string>('');
  readonly newAddress = signal<string>('');
  readonly newPrimaryCategory = signal<string>('Timber');
  readonly newLeadTimeDays = signal<number>(3);

  ngOnInit(): void {
    this.fetchSuppliers();
  }

  fetchSuppliers(): void {
    this.loading.set(true);
    this.errorMessage.set(null);
    this.service.listSuppliers().subscribe({
      next: (list) => {
        this.suppliers.set(list);
        this.loading.set(false);
      },
      error: (err) => {
        this.errorMessage.set(err?.error?.message || 'Failed to load suppliers directory.');
        this.loading.set(false);
      }
    });
  }

  selectCategory(cat: string): void {
    this.selectedCategory.set(cat);
  }

  onSearchInput(event: Event): void {
    const val = (event.target as HTMLInputElement).value;
    this.searchQuery.set(val);
  }

  toggleStatus(supplier: SupplierItem): void {
    const nextStatus = !supplier.isActive;
    this.service.updateSupplier(supplier.supplierId, {
      companyName: supplier.companyName,
      contactPerson: supplier.contactPerson,
      phone: supplier.phone,
      email: supplier.email,
      address: supplier.address,
      primaryCategory: supplier.primaryCategory,
      isActive: nextStatus
    }).subscribe({
      next: (updated) => {
        this.suppliers.update(list => list.map(s => s.supplierId === updated.supplierId ? updated : s));
      },
      error: (err) => {
        alert(err?.error?.message || 'Could not update supplier status.');
      }
    });
  }

  openRegisterModal(): void {
    this.modalError.set(null);
    const count = this.suppliers().length + 1;
    this.newSupplierCode.set(`SUP-NEW-0${count}`);
    this.newCompanyName.set('');
    this.newContactPerson.set('');
    this.newPhone.set('');
    this.newEmail.set('');
    this.newAddress.set('');
    this.newPrimaryCategory.set('Timber');
    this.newLeadTimeDays.set(3);
    this.showRegisterModal.set(true);
  }

  closeRegisterModal(): void {
    this.showRegisterModal.set(false);
  }

  submitNewSupplier(): void {
    if (!this.newSupplierCode().trim() || !this.newCompanyName().trim() || !this.newPhone().trim()) {
      this.modalError.set('Supplier code, company name, and phone number are required.');
      return;
    }

    this.submittingModal.set(true);
    this.modalError.set(null);

    this.service.createSupplier({
      supplierCode: this.newSupplierCode().trim(),
      companyName: this.newCompanyName().trim(),
      contactPerson: this.newContactPerson().trim(),
      phone: this.newPhone().trim(),
      email: this.newEmail().trim(),
      address: this.newAddress().trim(),
      primaryCategory: this.newPrimaryCategory(),
      initialLeadTimeDays: Number(this.newLeadTimeDays()) || 3
    }).subscribe({
      next: (created) => {
        this.suppliers.update(list => [...list, created]);
        this.submittingModal.set(false);
        this.closeRegisterModal();
      },
      error: (err) => {
        this.modalError.set(err?.error?.message || 'Failed to register new supplier.');
        this.submittingModal.set(false);
      }
    });
  }
}
