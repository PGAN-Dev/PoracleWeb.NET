import { Component, DestroyRef, effect, inject, model, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatAutocompleteModule, MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslateModule } from '@ngx-translate/core';
import { Subject } from 'rxjs';
import { debounceTime, distinctUntilChanged, filter, switchMap, tap } from 'rxjs/operators';

import { GymSearchResult, ScannerService } from '../../../core/services/scanner.service';

@Component({
  imports: [
    FormsModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslateModule,
  ],
  selector: 'app-gym-picker',
  standalone: true,
  styleUrl: './gym-picker.component.scss',
  templateUrl: './gym-picker.component.html',
})
export class GymPickerComponent {
  private readonly destroyRef = inject(DestroyRef);
  private initialized = false;
  private readonly scanner = inject(ScannerService);
  private readonly searchSubject = new Subject<string>();

  loading = signal(false);

  private readonly search$ = this.searchSubject.pipe(
    debounceTime(300),
    distinctUntilChanged(),
    filter(term => term.length >= 2),
    tap(() => this.loading.set(true)),
    switchMap(term => this.scanner.searchGyms(term)),
    tap(() => this.loading.set(false)),
  );

  gymId = model<string | null>(null);
  options = signal<GymSearchResult[]>([]);
  searchText = '';

  selectedGym = signal<GymSearchResult | null>(null);

  constructor() {
    this.search$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(results => this.options.set(results));

    // Load gym details when initialized with an existing gymId (edit mode)
    effect(() => {
      const id = this.gymId();
      if (id && !this.initialized && !this.selectedGym()) {
        this.initialized = true;
        this.scanner.getGymById(id).subscribe(gym => {
          if (gym) {
            this.selectedGym.set(gym);
            this.searchText = gym.name ?? gym.id;
          }
        });
      }
    });
  }

  clear(): void {
    this.selectedGym.set(null);
    this.searchText = '';
    this.options.set([]);
    this.gymId.set(null);
  }

  getTeamIcon(teamId: number | null): string {
    return `https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/gym/${teamId ?? 0}.png`;
  }

  onInput(value: string): void {
    this.searchSubject.next(value);
  }

  onSelected(event: MatAutocompleteSelectedEvent): void {
    const gym = event.option.value as GymSearchResult;
    this.selectedGym.set(gym);
    this.searchText = gym.name ?? gym.id;
    this.gymId.set(gym.id);
  }
}
