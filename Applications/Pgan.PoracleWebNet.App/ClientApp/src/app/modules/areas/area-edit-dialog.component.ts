import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { TranslateModule } from '@ngx-translate/core';

import { AreaDefinition } from '../../core/models';

export interface AreaEditDialogData {
  available: AreaDefinition[];
  selected: string[];
}

interface AreaItem {
  description?: string;
  group: string;
  name: string;
  selected: boolean;
}

interface AreaGroup {
  areas: AreaItem[];
  name: string;
  selectedCount: number;
  totalCount: number;
}

@Component({
  imports: [
    MatDialogModule,
    MatButtonModule,
    MatCheckboxModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatExpansionModule,
    FormsModule,
    TranslateModule,
  ],
  selector: 'app-area-edit-dialog',
  standalone: true,
  styleUrl: './area-edit-dialog.component.scss',
  templateUrl: './area-edit-dialog.component.html',
})
export class AreaEditDialogComponent implements OnInit {
  areas: AreaItem[] = [];
  readonly data = inject<AreaEditDialogData>(MAT_DIALOG_DATA);

  readonly dialogRef = inject(MatDialogRef<AreaEditDialogComponent>);
  readonly filteredGroups = signal<AreaGroup[]>([]);
  hasMultipleGroups = false;
  searchText = '';
  readonly selectedCount = signal(0);

  deselectAll(): void {
    for (const area of this.areas) area.selected = false;
    this.filterAreas();
  }

  deselectGroup(groupName: string): void {
    const key = groupName === 'Ungrouped' ? '' : groupName;
    for (const a of this.areas) {
      if (a.group === key) a.selected = false;
    }
    this.filterAreas();
  }

  filterAreas(): void {
    const search = this.searchText.toLowerCase();
    const filtered = this.areas.filter(a => !search || a.name.toLowerCase().includes(search));

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
      const allInGroup = this.areas.filter(a => a.group === key);
      groups.push({
        name: key || 'Ungrouped',
        areas,
        selectedCount: allInGroup.filter(a => a.selected).length,
        totalCount: allInGroup.length,
      });
    }

    this.filteredGroups.set(groups);
    this.selectedCount.set(this.areas.filter(a => a.selected).length);
  }

  ngOnInit(): void {
    const selectedSet = new Set(this.data.selected);
    this.areas = this.data.available
      .map(a => ({
        name: a.name,
        description: a.description,
        group: a.group ?? '',
        selected: selectedSet.has(a.name),
      }))
      .sort((a, b) => a.name.localeCompare(b.name));

    const groups = new Set(this.areas.map(a => a.group));
    this.hasMultipleGroups = groups.size > 1;
    this.filterAreas();
  }

  save(): void {
    const selected = this.areas.filter(a => a.selected).map(a => a.name);
    this.dialogRef.close(selected);
  }

  selectAll(): void {
    for (const area of this.areas) area.selected = true;
    this.filterAreas();
  }

  selectGroup(groupName: string): void {
    const key = groupName === 'Ungrouped' ? '' : groupName;
    for (const a of this.areas) {
      if (a.group === key) a.selected = true;
    }
    this.filterAreas();
  }

  toggle(area: AreaItem, checked: boolean): void {
    area.selected = checked;
    this.filterAreas();
  }
}
