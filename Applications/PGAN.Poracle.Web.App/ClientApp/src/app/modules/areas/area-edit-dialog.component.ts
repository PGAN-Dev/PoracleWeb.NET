import { Component, inject, OnInit, signal } from '@angular/core';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatExpansionModule } from '@angular/material/expansion';
import { FormsModule } from '@angular/forms';
import { AreaDefinition } from '../../core/models';

export interface AreaEditDialogData {
  available: AreaDefinition[];
  selected: string[];
}

interface AreaItem {
  name: string;
  group: string;
  description?: string;
  selected: boolean;
}

interface AreaGroup {
  name: string;
  areas: AreaItem[];
  selectedCount: number;
  totalCount: number;
}

@Component({
  selector: 'app-area-edit-dialog',
  standalone: true,
  imports: [
    MatDialogModule,
    MatButtonModule,
    MatCheckboxModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatExpansionModule,
    FormsModule,
  ],
  template: `
    <h2 mat-dialog-title>Select Areas</h2>
    <mat-dialog-content>
      <mat-form-field appearance="outline" class="search-field">
        <mat-label>Search areas</mat-label>
        <mat-icon matPrefix>search</mat-icon>
        <input matInput [(ngModel)]="searchText" (ngModelChange)="filterAreas()" placeholder="Filter by name..." />
        @if (searchText) {
          <button mat-icon-button matSuffix (click)="searchText = ''; filterAreas()">
            <mat-icon>close</mat-icon>
          </button>
        }
      </mat-form-field>

      <div class="select-actions">
        <button mat-button (click)="selectAll()">Select All</button>
        <button mat-button (click)="deselectAll()">Deselect All</button>
        <span class="selection-count">{{ selectedCount() }} selected</span>
      </div>

      @if (hasMultipleGroups) {
        <mat-accordion multi>
          @for (group of filteredGroups(); track group.name) {
            <mat-expansion-panel>
              <mat-expansion-panel-header>
                <mat-panel-title>
                  <mat-icon class="group-icon">folder</mat-icon>
                  {{ group.name }}
                  <span class="group-badge" [class.has-selected]="group.selectedCount > 0">
                    {{ group.selectedCount }}/{{ group.totalCount }}
                  </span>
                </mat-panel-title>
              </mat-expansion-panel-header>
              <div class="group-actions">
                <button mat-button color="primary" (click)="selectGroup(group.name)">Select All</button>
                <button mat-button (click)="deselectGroup(group.name)">Deselect All</button>
              </div>
              @for (area of group.areas; track area.name) {
                <mat-checkbox
                  [checked]="area.selected"
                  (change)="toggle(area, $event.checked)"
                  class="area-checkbox"
                >
                  {{ area.name }}
                </mat-checkbox>
              }
            </mat-expansion-panel>
          }
        </mat-accordion>
      } @else {
        @for (group of filteredGroups(); track group.name) {
          @for (area of group.areas; track area.name) {
            <mat-checkbox
              [checked]="area.selected"
              (change)="toggle(area, $event.checked)"
              class="area-checkbox"
            >
              {{ area.name }}
            </mat-checkbox>
          }
        }
      }

      @if (filteredGroups().length === 0) {
        <p class="no-results">No areas match your search</p>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Cancel</button>
      <button mat-raised-button color="primary" (click)="save()">
        <mat-icon>save</mat-icon> Save
      </button>
    </mat-dialog-actions>
  `,
  styles: [
    `
      .search-field {
        width: 100%;
      }
      .select-actions {
        display: flex;
        align-items: center;
        gap: 8px;
        margin-bottom: 16px;
      }
      .selection-count {
        margin-left: auto;
        font-size: 13px;
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
      }
      .group-icon {
        font-size: 18px;
        width: 18px;
        height: 18px;
        margin-right: 8px;
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
      }
      .group-badge {
        margin-left: 12px;
        font-size: 12px;
        font-weight: 500;
        padding: 2px 8px;
        border-radius: 12px;
        background: var(--divider, rgba(0, 0, 0, 0.08));
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
      }
      .group-badge.has-selected {
        background: #e8f5e9;
        color: #2e7d32;
      }
      .group-actions {
        display: flex;
        gap: 8px;
        margin-bottom: 8px;
        padding-bottom: 8px;
        border-bottom: 1px solid var(--divider, rgba(0, 0, 0, 0.08));
      }
      .area-checkbox {
        display: block;
        margin: 4px 0 4px 8px;
      }
      .no-results {
        text-align: center;
        color: var(--text-hint, rgba(0, 0, 0, 0.38));
        padding: 24px;
      }
    `,
  ],
})
export class AreaEditDialogComponent implements OnInit {
  readonly data = inject<AreaEditDialogData>(MAT_DIALOG_DATA);
  readonly dialogRef = inject(MatDialogRef<AreaEditDialogComponent>);

  searchText = '';
  areas: AreaItem[] = [];
  hasMultipleGroups = false;
  readonly filteredGroups = signal<AreaGroup[]>([]);
  readonly selectedCount = signal(0);

  ngOnInit(): void {
    const selectedSet = new Set(this.data.selected);
    this.areas = this.data.available
      .map((a) => ({
        name: a.name,
        group: a.group ?? '',
        description: a.description,
        selected: selectedSet.has(a.name),
      }))
      .sort((a, b) => a.name.localeCompare(b.name));

    const groups = new Set(this.areas.map((a) => a.group));
    this.hasMultipleGroups = groups.size > 1;
    this.filterAreas();
  }

  filterAreas(): void {
    const search = this.searchText.toLowerCase();
    const filtered = this.areas.filter(
      (a) => !search || a.name.toLowerCase().includes(search),
    );

    const groupMap = new Map<string, AreaItem[]>();
    for (const area of filtered) {
      const key = area.group;
      if (!groupMap.has(key)) groupMap.set(key, []);
      groupMap.get(key)!.push(area);
    }

    const groups: AreaGroup[] = [];
    const sortedKeys = [...groupMap.keys()].sort((a, b) => {
      if (a === '') return -1;
      if (b === '') return 1;
      return a.localeCompare(b);
    });

    for (const key of sortedKeys) {
      const areas = groupMap.get(key)!;
      const allInGroup = this.areas.filter((a) => a.group === key);
      groups.push({
        name: key || 'Ungrouped',
        areas,
        selectedCount: allInGroup.filter((a) => a.selected).length,
        totalCount: allInGroup.length,
      });
    }

    this.filteredGroups.set(groups);
    this.selectedCount.set(this.areas.filter((a) => a.selected).length);
  }

  toggle(area: AreaItem, checked: boolean): void {
    area.selected = checked;
    this.filterAreas();
  }

  selectAll(): void {
    for (const area of this.areas) area.selected = true;
    this.filterAreas();
  }

  deselectAll(): void {
    for (const area of this.areas) area.selected = false;
    this.filterAreas();
  }

  selectGroup(groupName: string): void {
    const key = groupName === 'Ungrouped' ? '' : groupName;
    for (const a of this.areas) {
      if (a.group === key) a.selected = true;
    }
    this.filterAreas();
  }

  deselectGroup(groupName: string): void {
    const key = groupName === 'Ungrouped' ? '' : groupName;
    for (const a of this.areas) {
      if (a.group === key) a.selected = false;
    }
    this.filterAreas();
  }

  save(): void {
    const selected = this.areas.filter((a) => a.selected).map((a) => a.name);
    this.dialogRef.close(selected);
  }
}
