import { ChangeDetectionStrategy, Component, computed, DestroyRef, inject, OnInit, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';

import { CrossProfileAlarm, CrossProfileOverview, CrossProfileProfile, Profile } from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { CrossProfileService } from '../../core/services/cross-profile.service';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { ProfileService } from '../../core/services/profile.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { getDisplayName as getGruntDisplayName } from '../invasions/invasion.constants';
import { ProfileAddDialogComponent } from '../profiles/profile-add-dialog.component';
import { ProfileEditDialogComponent } from '../profiles/profile-edit-dialog.component';

interface AlarmTypeConfig {
  color: string;
  icon: string;
  key: string;
  label: string;
}

interface ProfileGroup {
  alarmsByType: Map<string, CrossProfileAlarm[]>;
  profile: CrossProfileProfile;
  totalAlarms: number;
}

interface DuplicateInfo {
  alarm: CrossProfileAlarm;
  profileNames: string[];
  type: string;
}

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    MatBadgeModule,
    MatButtonModule,
    MatCardModule,
    MatChipsModule,
    MatDialogModule,
    MatExpansionModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSnackBarModule,
    MatTooltipModule,
  ],
  selector: 'app-cross-profile-overview',
  standalone: true,
  styleUrl: './cross-profile-overview.component.scss',
  templateUrl: './cross-profile-overview.component.html',
})
export class CrossProfileOverviewComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly crossProfileService = inject(CrossProfileService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly masterData = inject(MasterDataService);
  private readonly profileService = inject(ProfileService);
  private readonly snackBar = inject(MatSnackBar);
  readonly activeProfileNo = signal<number>(1);

  readonly alarmTypes: AlarmTypeConfig[] = [
    { color: '#4caf50', icon: 'catching_pokemon', key: 'pokemon', label: 'Pokemon' },
    { color: '#f44336', icon: 'shield', key: 'raid', label: 'Raids' },
    { color: '#ff9800', icon: 'egg', key: 'egg', label: 'Eggs' },
    { color: '#d500f9', icon: 'flash_on', key: 'maxbattle', label: 'Max Battles' },
    { color: '#9c27b0', icon: 'assignment', key: 'quest', label: 'Quests' },
    { color: '#607d8b', icon: 'warning', key: 'invasion', label: 'Invasions' },
    { color: '#e91e63', icon: 'place', key: 'lure', label: 'Lures' },
    { color: '#8bc34a', icon: 'park', key: 'nest', label: 'Nests' },
    { color: '#00bcd4', icon: 'fitness_center', key: 'gym', label: 'Gyms' },
    { color: '#795548', icon: 'domain', key: 'fort', label: 'Fort Changes' },
  ];

  readonly overview = signal<CrossProfileOverview | null>(null);

  readonly duplicates = computed<DuplicateInfo[]>(() => {
    const data = this.overview();
    if (!data) return [];

    const duplicates: DuplicateInfo[] = [];
    const profileMap = new Map(data.profile.map(p => [p.profile_no, p.name]));

    for (const type of this.alarmTypes) {
      const alarms = (data[type.key as keyof CrossProfileOverview] as CrossProfileAlarm[] | undefined) ?? [];
      const keyMap = new Map<string, CrossProfileAlarm[]>();

      for (const alarm of alarms) {
        const key = this.getAlarmKey(alarm, type.key);
        const existing = keyMap.get(key) ?? [];
        existing.push(alarm);
        keyMap.set(key, existing);
      }

      for (const [, group] of keyMap) {
        if (group.length > 1) {
          duplicates.push({
            alarm: group[0],
            profileNames: group.map(a => profileMap.get(a.profile_no) ?? `Profile ${a.profile_no}`),
            type: type.key,
          });
        }
      }
    }

    return duplicates;
  });

  readonly duplicateUids = computed<Set<number>>(() => {
    const data = this.overview();
    if (!data) return new Set();

    const uidSet = new Set<number>();

    for (const type of this.alarmTypes) {
      const alarms = (data[type.key as keyof CrossProfileOverview] as CrossProfileAlarm[] | undefined) ?? [];
      const keyMap = new Map<string, CrossProfileAlarm[]>();

      for (const alarm of alarms) {
        const key = this.getAlarmKey(alarm, type.key);
        const existing = keyMap.get(key) ?? [];
        existing.push(alarm);
        keyMap.set(key, existing);
      }

      for (const [, group] of keyMap) {
        if (group.length > 1) {
          for (const alarm of group) {
            uidSet.add(alarm.uid);
          }
        }
      }
    }

    return uidSet;
  });

  readonly expandedProfiles = signal(new Set<number>());

  readonly managedProfiles = signal<Profile[]>([]);

  readonly profiles = computed<ProfileGroup[]>(() => {
    const data = this.overview();
    const managed = this.managedProfiles();

    // Build profile list from managedProfiles (authoritative), enriched with alarm data from overview
    const overviewProfiles = data?.profile ?? [];
    const profileList: CrossProfileProfile[] = managed.map(mp => {
      const op = overviewProfiles.find(p => p.profile_no === mp.profileNo);
      return op ?? { id: '', name: mp.name ?? `Profile ${mp.profileNo}`, profile_no: mp.profileNo };
    });

    // Add any profiles from overview that aren't in managed (shouldn't happen, but be safe)
    for (const op of overviewProfiles) {
      if (!profileList.some(p => p.profile_no === op.profile_no)) {
        profileList.push(op);
      }
    }

    return profileList.map(profile => {
      const alarmsByType = new Map<string, CrossProfileAlarm[]>();
      let totalAlarms = 0;

      if (data) {
        for (const type of this.alarmTypes) {
          const alarms = ((data[type.key as keyof CrossProfileOverview] as CrossProfileAlarm[] | undefined) ?? []).filter(
            a => a.profile_no === profile.profile_no,
          );
          if (alarms.length > 0) {
            alarmsByType.set(type.key, alarms);
            totalAlarms += alarms.length;
          }
        }
      }

      return { alarmsByType, profile, totalAlarms };
    });
  });

  readonly searchTerm = signal('');

  readonly selectedType = signal<string | null>(null);
  readonly showDuplicatesOnly = signal(false);

  readonly filteredProfiles = computed<ProfileGroup[]>(() => {
    const search = this.searchTerm().toLowerCase();
    const typeFilter = this.selectedType();
    const dupsOnly = this.showDuplicatesOnly();
    const dupUids = dupsOnly ? this.duplicateUids() : null;
    const allProfiles = this.profiles();

    return allProfiles
      .map(group => {
        const filtered = new Map<string, CrossProfileAlarm[]>();
        let total = 0;

        for (const [type, alarms] of group.alarmsByType) {
          if (typeFilter && type !== typeFilter) continue;

          let matchingAlarms = search ? alarms.filter(a => this.alarmMatchesSearch(a, type, search)) : alarms;
          if (dupUids) matchingAlarms = matchingAlarms.filter(a => dupUids.has(a.uid));

          if (matchingAlarms.length > 0) {
            filtered.set(type, matchingAlarms);
            total += matchingAlarms.length;
          }
        }

        return { alarmsByType: filtered, profile: group.profile, totalAlarms: total };
      })
      .filter(g => g.totalAlarms > 0 || (!search && !typeFilter && !dupsOnly));
  });

  readonly iconService = inject(IconService);

  readonly loading = signal(true);

  readonly searchControl = new FormControl('');

  readonly skeletonPanels = Array.from({ length: 3 });

  readonly stats = computed(() => {
    const data = this.overview();
    if (!data) return null;

    const typeCounts: Record<string, number> = {};
    let totalAlarms = 0;

    for (const type of this.alarmTypes) {
      const count = ((data[type.key as keyof CrossProfileOverview] as CrossProfileAlarm[] | undefined) ?? []).length;
      typeCounts[type.key] = count;
      totalAlarms += count;
    }

    return {
      duplicateCount: this.duplicates().length,
      profileCount: data.profile.length,
      totalAlarms,
      typeCounts,
    };
  });

  readonly switching = signal(false);

  deleteProfile(profile: Profile): void {
    if (profile.active) return;
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: 'Delete',
        message: `Are you sure you want to delete profile "${profile.name}" (#${profile.profileNo})? All alarms in this profile will be lost.`,
        title: 'Delete Profile',
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.profileService.delete(profile.profileNo).subscribe({
          error: () => this.snackBar.open('Failed to delete profile', 'OK', { duration: 3000 }),
          next: () => {
            this.snackBar.open('Profile deleted', 'OK', { duration: 3000 });
            this.loadAll();
          },
        });
      }
    });
  }

  duplicateProfile(profile: CrossProfileProfile): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      width: '400px',
      data: {
        confirmText: 'Duplicate',
        message: `Create a new profile with all of ${profile.name}'s alarms copied over.`,
        promptField: {
          existingNames: this.managedProfiles().map(p => p.name),
          label: 'New Profile Name',
          value: `${profile.name} (Copy)`,
        },
        title: 'Duplicate Profile',
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe((name: string | false) => {
      if (name) {
        this.switching.set(true);
        this.crossProfileService
          .duplicateProfile(profile.profile_no, name)
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            error: () => {
              this.switching.set(false);
              this.snackBar.open('Failed to duplicate profile', 'OK', { duration: 3000 });
            },
            next: res => {
              this.switching.set(false);
              if (res.token) {
                this.authService.setToken(res.token);
              }
              this.snackBar.open(`Duplicated profile with ${res.alarmsCopied} alarms`, 'OK', { duration: 3000 });
              this.loadAll();
            },
          });
      }
    });
  }

  editProfile(profile: Profile): void {
    const ref = this.dialog.open(ProfileEditDialogComponent, { width: '400px', data: profile });
    ref.afterClosed().subscribe(result => {
      if (result) this.loadAll();
    });
  }

  exportProfile(profile: CrossProfileProfile): void {
    const data = this.overview();
    if (!data) return;

    const internalKeys = new Set(['uid', 'id', 'profile_no', 'description']);
    const stripInternal = (alarm: CrossProfileAlarm): Record<string, unknown> => {
      const cleaned: Record<string, unknown> = {};
      for (const [key, value] of Object.entries(alarm)) {
        if (!internalKeys.has(key)) cleaned[key] = value;
      }
      return cleaned;
    };

    const alarms: Record<string, Record<string, unknown>[]> = {};
    for (const type of this.alarmTypes) {
      const typeAlarms = ((data[type.key as keyof CrossProfileOverview] as CrossProfileAlarm[] | undefined) ?? []).filter(
        a => a.profile_no === profile.profile_no,
      );
      if (typeAlarms.length > 0) {
        alarms[type.key] = typeAlarms.map(stripInternal);
      }
    }

    const backup = {
      alarms,
      exportedAt: new Date().toISOString(),
      profileName: profile.name,
      version: 1,
    };

    const blob = new Blob([JSON.stringify(backup, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `profile-${profile.name.toLowerCase().replace(/\s+/g, '-')}.json`;
    a.click();
    URL.revokeObjectURL(url);
    this.snackBar.open(`Exported "${profile.name}" backup`, 'OK', { duration: 3000 });
  }

  formatDistance(meters: number): string {
    if (meters >= 1000) {
      return `${(meters / 1000).toFixed(1)} km`;
    }
    return `${meters} m`;
  }

  getAlarmDescription(alarm: CrossProfileAlarm, type: string): string {
    switch (type) {
      case 'pokemon': {
        const id = alarm.pokemon_id ?? 0;
        if (id === 0) return 'All Pokemon';
        const name = this.masterData.getPokemonName(id);
        const form = this.masterData.getFormName(id, alarm.form ?? 0);
        return form ? `${name} (${form})` : name;
      }
      case 'raid': {
        const id = alarm.pokemon_id ?? alarm.raid_pokemon_id ?? 0;
        if (id > 0 && id !== 9000) return this.masterData.getPokemonName(id);
        const level = alarm.level ?? 9000;
        if (level === 9000) return 'All Raids';
        return level === 6 ? 'All Mega Raids' : `All Level ${level} Raids`;
      }
      case 'egg': {
        const level = alarm.level ?? 9000;
        if (level === 9000) return 'All Eggs';
        return level === 6 ? 'Mega Egg' : `Level ${level} Egg`;
      }
      case 'quest': {
        const rt = alarm.reward_type ?? 0;
        const id = alarm.pokemon_id ?? alarm.reward ?? 0;
        if (rt === 7 && id > 0) return this.masterData.getPokemonName(id);
        if (rt === 7) return 'Any Pokemon Encounter';
        if (rt === 12 && id > 0) return `${this.masterData.getPokemonName(id)} Mega Energy`;
        if (rt === 4 && id > 0) return `${this.masterData.getPokemonName(id)} Candy`;
        if (rt === 3) return alarm.amount ? `${alarm.amount}+ Stardust` : 'Stardust';
        if (rt === 2 && (alarm.reward ?? 0) > 0) return this.masterData.getItemName(alarm.reward!);
        if (rt === 2) return 'Item Reward';
        return 'Quest Reward';
      }
      case 'invasion':
        return getGruntDisplayName(alarm.grunt_type ?? null);
      case 'lure':
        return this.getLureName(alarm.lure_id ?? 0);
      case 'nest': {
        const id = alarm.pokemon_id ?? 0;
        if (id === 0) return 'Any Nest';
        const name = this.masterData.getPokemonName(id);
        return `${name} Nest`;
      }
      case 'gym': {
        const team = alarm.team ?? 0;
        if (alarm.slot_changes || alarm.battle_changes) return 'Gym Activity';
        if (team === 0) return 'Any Team';
        return this.getTeamName(team);
      }
      case 'maxbattle': {
        const id = alarm.pokemon_id ?? 9000;
        if (id > 0 && id !== 9000) return this.masterData.getPokemonName(id);
        const level = alarm.level ?? 0;
        return this.getMaxBattleLabel(level);
      }
      case 'fort': {
        const fortType = this.formatFortType(alarm.fort_type ?? null);
        const changes = this.formatChangeTypes(alarm.change_types ?? null);
        return changes ? `${fortType} · ${changes}` : fortType;
      }
      default:
        return 'Alarm';
    }
  }

  getAlarmImage(alarm: CrossProfileAlarm, type: string): string {
    const pokemonId = alarm.pokemon_id ?? alarm.raid_pokemon_id ?? 0;
    const form = alarm.form ?? 0;

    switch (type) {
      case 'pokemon':
      case 'nest':
        return pokemonId > 0 && pokemonId !== 9000 ? this.iconService.getPokemonUrl(pokemonId, form) : '';
      case 'raid':
        return pokemonId > 0 && pokemonId !== 9000
          ? this.iconService.getPokemonUrl(pokemonId, form)
          : this.iconService.getRaidEggUrl(alarm.level ?? 5);
      case 'egg':
        return this.iconService.getRaidEggUrl(alarm.level ?? 5);
      case 'quest': {
        const rt = alarm.reward_type ?? 0;
        const id = alarm.pokemon_id ?? alarm.reward ?? 0;
        if (rt === 7 && id > 0) return this.iconService.getPokemonUrl(id);
        if (rt === 2 && (alarm.reward ?? 0) > 0) return this.iconService.getItemUrl(alarm.reward!);
        return '';
      }
      case 'lure':
        return alarm.lure_id ? `https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/reward/item/${alarm.lure_id}.png` : '';
      case 'gym':
        return this.iconService.getGymUrl(alarm.team ?? 0);
      case 'maxbattle':
        return pokemonId > 0 && pokemonId !== 9000 ? this.iconService.getPokemonUrl(pokemonId, form) : '';
      case 'invasion':
        return '';
      default:
        return '';
    }
  }

  getAlarmKey(alarm: CrossProfileAlarm, type: string): string {
    switch (type) {
      case 'pokemon':
        return `pokemon:${alarm.pokemon_id ?? 0}:${alarm.form ?? 0}`;
      case 'raid':
        return `raid:${alarm.pokemon_id ?? 0}:${alarm.level ?? 0}:${alarm.form ?? 0}`;
      case 'egg':
        return `egg:${alarm.level ?? 0}`;
      case 'quest':
        return `quest:${alarm.pokemon_id ?? 0}:${alarm.reward_type ?? 0}:${alarm.reward ?? 0}`;
      case 'invasion':
        return `invasion:${alarm.grunt_type ?? ''}`;
      case 'lure':
        return `lure:${alarm.lure_id ?? 0}`;
      case 'nest':
        return `nest:${alarm.pokemon_id ?? 0}`;
      case 'gym':
        return `gym:${alarm.gym_id ?? ''}:${alarm.team ?? 0}`;
      case 'maxbattle':
        return `maxbattle:${alarm.pokemon_id ?? 0}:${alarm.level ?? 0}`;
      case 'fort':
        return `fort:${alarm.fort_type ?? ''}:${alarm.change_types ?? ''}`;
      default:
        return `${type}:${alarm.uid}`;
    }
  }

  getDuplicateProfiles(alarm: CrossProfileAlarm, type: string): string[] {
    const data = this.overview();
    if (!data) return [];

    const key = this.getAlarmKey(alarm, type);
    const alarms = (data[type as keyof CrossProfileOverview] as CrossProfileAlarm[] | undefined) ?? [];
    const profileMap = new Map(data.profile.map(p => [p.profile_no, p.name]));

    return alarms
      .filter(a => this.getAlarmKey(a, type) === key && a.profile_no !== alarm.profile_no)
      .map(a => profileMap.get(a.profile_no) ?? `Profile ${a.profile_no}`);
  }

  getManagedProfile(profileNo: number): Profile | undefined {
    return this.managedProfiles().find(p => p.profileNo === profileNo);
  }

  getTypeConfig(key: string): AlarmTypeConfig {
    return this.alarmTypes.find(t => t.key === key) ?? this.alarmTypes[0];
  }

  getTypeCount(typeKey: string): number {
    return this.stats()?.typeCounts[typeKey] ?? 0;
  }

  importProfile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;

    const reader = new FileReader();
    reader.onload = () => {
      input.value = '';
      try {
        const backup = JSON.parse(reader.result as string);
        if (!backup.version || !backup.profileName || !backup.alarms) {
          this.snackBar.open('Invalid backup file format', 'OK', { duration: 3000 });
          return;
        }

        const ref = this.dialog.open(ConfirmDialogComponent, {
          width: '400px',
          data: {
            confirmText: 'Import',
            message: 'Create a new profile with all alarms from this backup.',
            promptField: {
              existingNames: this.managedProfiles().map(p => p.name),
              label: 'Profile Name',
              value: backup.profileName,
            },
            title: 'Import Profile',
          } as ConfirmDialogData,
        });
        ref.afterClosed().subscribe((name: string | false) => {
          if (name) {
            this.switching.set(true);
            this.crossProfileService
              .importProfile({ ...backup, profileName: name })
              .pipe(takeUntilDestroyed(this.destroyRef))
              .subscribe({
                error: () => {
                  this.switching.set(false);
                  this.snackBar.open('Failed to import profile', 'OK', { duration: 3000 });
                },
                next: res => {
                  this.switching.set(false);
                  if (res.token) this.authService.setToken(res.token);
                  this.snackBar.open(`Imported "${name}" with ${res.alarmsCopied} alarms`, 'OK', { duration: 3000 });
                  this.loadAll();
                },
              });
          }
        });
      } catch {
        this.snackBar.open('Could not read backup file', 'OK', { duration: 3000 });
      }
    };
    reader.readAsText(file);
  }

  isActiveProfile(profileNo: number): boolean {
    return this.activeProfileNo() === profileNo;
  }

  isDuplicate(alarm: CrossProfileAlarm): boolean {
    return this.duplicateUids().has(alarm.uid);
  }

  ngOnInit(): void {
    this.masterData.loadData().pipe(takeUntilDestroyed(this.destroyRef)).subscribe();
    this.loadAll();

    this.searchControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(value => {
      this.searchTerm.set(value ?? '');
    });
  }

  onImageError(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }

  onPanelClosed(profileNo: number): void {
    const current = new Set(this.expandedProfiles());
    current.delete(profileNo);
    this.expandedProfiles.set(current);
  }

  onPanelOpened(profileNo: number): void {
    const current = new Set(this.expandedProfiles());
    current.add(profileNo);
    this.expandedProfiles.set(current);
  }

  openAddProfileDialog(): void {
    const ref = this.dialog.open(ProfileAddDialogComponent, { width: '400px' });
    ref.afterClosed().subscribe(result => {
      if (result) this.loadAll();
    });
  }

  setTypeFilter(type: string | null): void {
    this.selectedType.set(this.selectedType() === type ? null : type);
  }

  switchProfile(profile: CrossProfileProfile): void {
    this.switching.set(true);
    this.profileService
      .switchProfile(profile.profile_no)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.switching.set(false);
          this.snackBar.open('Failed to switch profile', 'OK', { duration: 3000 });
        },
        next: res => {
          this.switching.set(false);
          if (res.token) {
            this.authService.setToken(res.token);
          }
          this.snackBar.open(`Switched to profile "${profile.name}"`, 'OK', { duration: 3000 });
          this.authService.loadCurrentUser();
          this.loadAll();
        },
      });
  }

  toggleDuplicatesFilter(): void {
    this.showDuplicatesOnly.update(v => !v);
  }

  private alarmMatchesSearch(alarm: CrossProfileAlarm, type: string, search: string): boolean {
    const desc = this.getAlarmDescription(alarm, type).toLowerCase();
    if (desc.includes(search)) return true;

    if (alarm.description?.toLowerCase().includes(search)) return true;

    const pokemonId = alarm.pokemon_id ?? 0;
    if (pokemonId > 0 && String(pokemonId).includes(search)) return true;

    return false;
  }

  private formatChangeTypes(raw: string | null): string {
    if (!raw) return '';
    try {
      const types: string[] = JSON.parse(raw);
      const labels: Record<string, string> = { name: 'Name', image_url: 'Image', location: 'Location', new: 'New', removal: 'Removal' };
      return types.map(t => labels[t] ?? t).join(', ');
    } catch {
      return raw;
    }
  }

  private formatFortType(type: string | null): string {
    switch (type) {
      case 'pokestop':
        return 'Pokestop Changes';
      case 'gym':
        return 'Gym Changes';
      default:
        return 'All Fort Changes';
    }
  }

  private getLureName(id: number): string {
    switch (id) {
      case 0:
        return 'All Lures';
      case 501:
        return 'Normal Lure';
      case 502:
        return 'Glacial Lure';
      case 503:
        return 'Mossy Lure';
      case 504:
        return 'Magnetic Lure';
      case 505:
        return 'Rainy Lure';
      case 506:
        return 'Golden Lure';
      default:
        return `Lure #${id}`;
    }
  }

  private getMaxBattleLabel(level: number): string {
    switch (level) {
      case 1:
        return '1 Star Max Battle';
      case 2:
        return '2 Star Max Battle';
      case 3:
        return '3 Star Max Battle';
      case 4:
        return '4 Star Max Battle';
      case 5:
        return '5 Star Max Battle';
      case 7:
        return 'Gigantamax';
      case 8:
        return 'Legendary Gigantamax';
      default:
        return 'Any Max Battle';
    }
  }

  private getTeamName(team: number): string {
    switch (team) {
      case 1:
        return 'Mystic';
      case 2:
        return 'Valor';
      case 3:
        return 'Instinct';
      default:
        return `Team ${team}`;
    }
  }

  private loadAll(): void {
    this.loading.set(true);
    this.profileService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: profiles => {
          this.managedProfiles.set(profiles);
          const active = profiles.find(p => p.active);
          if (active) this.activeProfileNo.set(active.profileNo);
        },
      });
    this.crossProfileService
      .getOverview()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.loading.set(false);
          this.snackBar.open('Failed to load cross-profile overview', 'OK', { duration: 3000 });
        },
        next: data => {
          this.overview.set(data);
          this.loading.set(false);

          this.expandedProfiles.set(new Set());
        },
      });
  }
}
