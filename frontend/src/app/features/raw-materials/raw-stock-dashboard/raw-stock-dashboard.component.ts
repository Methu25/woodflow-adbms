import { CommonModule } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { RawMaterialsService } from '../services/raw-materials.service';

@Component({
  selector: 'app-raw-stock-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './raw-stock-dashboard.component.html',
  styleUrls: ['../raw-materials-header.scss', './raw-stock-dashboard.component.scss']
})
export class RawStockDashboardComponent implements OnInit {
  readonly service = inject(RawMaterialsService);

  readonly categories = ['All', 'Timber', 'Boards', 'Hardware', 'Finishing', 'Fabric', 'Adhesives'];
  readonly selectedCategory = signal<string>('All');
  readonly searchQuery = signal<string>('');

  ngOnInit(): void {
    this.fetchData();
  }

  selectCategory(cat: string): void {
    this.selectedCategory.set(cat);
    this.fetchData();
  }

  onSearchInput(event: Event): void {
    const query = (event.target as HTMLInputElement).value;
    this.searchQuery.set(query);
    this.fetchData();
  }

  private fetchData(): void {
    const cat = this.selectedCategory() === 'All' ? undefined : this.selectedCategory();
    const q = this.searchQuery();
    this.service.loadDashboard(cat, q).subscribe({ error: () => {} });
  }
}
