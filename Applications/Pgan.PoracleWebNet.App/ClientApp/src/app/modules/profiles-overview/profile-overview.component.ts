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
import { TranslateModule } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';

import {
  ActiveHourEntry,
  parseActiveHours,
  ProfileOverviewAlarm,
  ProfileOverview,
  ProfileOverviewProfile,
  Profile,
} from '../../core/models';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { IconService } from '../../core/services/icon.service';
import { MasterDataService } from '../../core/services/masterdata.service';
import { ProfileOverviewService } from '../../core/services/profile-overview.service';
import { ProfileService } from '../../core/services/profile.service';
import { ActiveHoursChipComponent } from '../../shared/components/active-hours-chip/active-hours-chip.component';
import {
  ActiveHoursEditorData,
  ActiveHoursEditorDialogComponent,
} from '../../shared/components/active-hours-editor-dialog/active-hours-editor-dialog.component';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { LocationWarningComponent } from '../../shared/components/location-warning/location-warning.component';
import { getGruntDisplayName } from '../invasions/invasion.constants';
import { ProfileAddDialogComponent } from '../profiles/profile-add-dialog.component';
import { ProfileEditDialogComponent } from '../profiles/profile-edit-dialog.component';

interface AlarmTypeConfig {
  color: string;
  icon: string;
  key: string;
  label: string;
}

interface ProfileGroup {
  alarmsByType: Map<string, ProfileOverviewAlarm[]>;
  profile: ProfileOverviewProfile;
  totalAlarms: number;
}

interface DuplicateInfo {
  alarm: ProfileOverviewAlarm;
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
    TranslateModule,
    ActiveHoursChipComponent,
    LocationWarningComponent,
  ],
  selector: 'app-profile-overview',
  standalone: true,
  styleUrl: './profile-overview.component.scss',
  templateUrl: './profile-overview.component.html',
})
export class ProfileOverviewComponent implements OnInit {
  private readonly authService = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly i18n = inject(I18nService);
  private readonly masterData = inject(MasterDataService);
  private readonly profileOverviewService = inject(ProfileOverviewService);
  private readonly profileService = inject(ProfileService);
  private readonly snackBar = inject(MatSnackBar);
  readonly activeProfileNo = signal<number>(1);

  readonly alarmTypes: AlarmTypeConfig[] = [
    { color: '#4caf50', icon: 'catching_pokemon', key: 'pokemon', label: this.i18n.instant('PROFILES.TYPE_POKEMON') },
    { color: '#f44336', icon: 'shield', key: 'raid', label: this.i18n.instant('PROFILES.TYPE_RAIDS') },
    { color: '#ff9800', icon: 'egg', key: 'egg', label: this.i18n.instant('PROFILES.TYPE_EGGS') },
    { color: '#d500f9', icon: 'flash_on', key: 'maxbattle', label: this.i18n.instant('PROFILES.TYPE_MAX_BATTLES') },
    { color: '#9c27b0', icon: 'assignment', key: 'quest', label: this.i18n.instant('PROFILES.TYPE_QUESTS') },
    { color: '#607d8b', icon: 'warning', key: 'invasion', label: this.i18n.instant('PROFILES.TYPE_INVASIONS') },
    { color: '#e91e63', icon: 'place', key: 'lure', label: this.i18n.instant('PROFILES.TYPE_LURES') },
    { color: '#8bc34a', icon: 'park', key: 'nest', label: this.i18n.instant('PROFILES.TYPE_NESTS') },
    { color: '#00bcd4', icon: 'fitness_center', key: 'gym', label: this.i18n.instant('PROFILES.TYPE_GYMS') },
    { color: '#795548', icon: 'domain', key: 'fort', label: this.i18n.instant('PROFILES.TYPE_FORT_CHANGES') },
  ];

  readonly overview = signal<ProfileOverview | null>(null);

  readonly duplicates = computed<DuplicateInfo[]>(() => {
    const data = this.overview();
    if (!data) return [];

    const duplicates: DuplicateInfo[] = [];
    const profileMap = new Map(data.profile.map(p => [p.profile_no, p.name]));

    for (const type of this.alarmTypes) {
      const alarms = (data[type.key as keyof ProfileOverview] as ProfileOverviewAlarm[] | undefined) ?? [];
      const keyMap = new Map<string, ProfileOverviewAlarm[]>();

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
      const alarms = (data[type.key as keyof ProfileOverview] as ProfileOverviewAlarm[] | undefined) ?? [];
      const keyMap = new Map<string, ProfileOverviewAlarm[]>();

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
    const profileList: ProfileOverviewProfile[] = managed.map(mp => {
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
      const alarmsByType = new Map<string, ProfileOverviewAlarm[]>();
      let totalAlarms = 0;

      if (data) {
        for (const type of this.alarmTypes) {
          const alarms = ((data[type.key as keyof ProfileOverview] as ProfileOverviewAlarm[] | undefined) ?? []).filter(
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
        const filtered = new Map<string, ProfileOverviewAlarm[]>();
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

  readonly parseActiveHours = parseActiveHours;

  readonly searchControl = new FormControl('');

  readonly skeletonPanels = Array.from({ length: 3 });

  readonly stats = computed(() => {
    const data = this.overview();
    if (!data) return null;

    const typeCounts: Record<string, number> = {};
    let totalAlarms = 0;

    for (const type of this.alarmTypes) {
      const count = ((data[type.key as keyof ProfileOverview] as ProfileOverviewAlarm[] | undefined) ?? []).length;
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
        confirmText: this.i18n.instant('COMMON.DELETE'),
        message: this.i18n.instant('PROFILES.CONFIRM_DELETE_MSG', { name: profile.name, number: profile.profileNo }),
        title: this.i18n.instant('PROFILES.CONFIRM_DELETE_TITLE'),
        warn: true,
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.profileService.delete(profile.profileNo).subscribe({
          error: () =>
            this.snackBar.open(this.i18n.instant('PROFILES.SNACK_FAILED_DELETE'), this.i18n.instant('TOAST.OK'), { duration: 3000 }),
          next: () => {
            this.snackBar.open(this.i18n.instant('PROFILES.SNACK_DELETED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
            this.loadAll();
          },
        });
      }
    });
  }

  duplicateProfile(profile: ProfileOverviewProfile): void {
    const ref = this.dialog.open(ConfirmDialogComponent, {
      width: '400px',
      data: {
        confirmText: this.i18n.instant('PROFILES.DUPLICATE'),
        message: this.i18n.instant('PROFILES.DUPLICATE_DESC', { name: profile.name }),
        promptField: {
          existingNames: this.managedProfiles().map(p => p.name),
          label: this.i18n.instant('PROFILES.NEW_PROFILE_NAME'),
          value: `${profile.name} (Copy)`,
        },
        title: this.i18n.instant('PROFILES.DUPLICATE_DIALOG_TITLE'),
      } as ConfirmDialogData,
    });
    ref.afterClosed().subscribe((name: string | false) => {
      if (name) {
        this.switching.set(true);
        this.profileOverviewService
          .duplicateProfile(profile.profile_no, name)
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({
            error: () => {
              this.switching.set(false);
              this.snackBar.open(this.i18n.instant('PROFILES.SNACK_FAILED_DUPLICATE'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
            },
            next: res => {
              this.switching.set(false);
              if (res.token) {
                this.authService.setToken(res.token);
              }
              this.snackBar.open(
                this.i18n.instant('PROFILES.SNACK_DUPLICATED', { count: res.alarmsCopied }),
                this.i18n.instant('TOAST.OK'),
                { duration: 3000 },
              );
              this.loadAll();
            },
          });
      }
    });
  }

  editActiveHours(profile: ProfileOverviewProfile): void {
    const entries = parseActiveHours(profile.active_hours);
    const ref = this.dialog.open(ActiveHoursEditorDialogComponent, {
      maxWidth: '95vw',
      width: '560px',
      data: { activeHours: entries, profileName: profile.name } as ActiveHoursEditorData,
    });
    ref.afterClosed().subscribe((result: ActiveHourEntry[] | null | undefined) => {
      if (result !== null && result !== undefined) {
        this.profileService.updateActiveHours(profile.profile_no, result).subscribe({
          error: () => {
            this.snackBar.open(this.i18n.instant('PROFILES.SNACK_FAILED_SCHEDULE'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          },
          next: () => {
            this.snackBar.open(this.i18n.instant('PROFILES.SNACK_SCHEDULE_UPDATED'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
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

  exportProfile(profile: ProfileOverviewProfile): void {
    const data = this.overview();
    if (!data) return;

    const internalKeys = new Set(['uid', 'id', 'profile_no', 'description']);
    const stripInternal = (alarm: ProfileOverviewAlarm): Record<string, unknown> => {
      const cleaned: Record<string, unknown> = {};
      for (const [key, value] of Object.entries(alarm)) {
        if (!internalKeys.has(key)) cleaned[key] = value;
      }
      return cleaned;
    };

    const alarms: Record<string, Record<string, unknown>[]> = {};
    for (const type of this.alarmTypes) {
      const typeAlarms = ((data[type.key as keyof ProfileOverview] as ProfileOverviewAlarm[] | undefined) ?? []).filter(
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
    this.snackBar.open(this.i18n.instant('PROFILES.SNACK_EXPORTED', { name: profile.name }), this.i18n.instant('TOAST.OK'), {
      duration: 3000,
    });
  }

  formatDistance(meters: number): string {
    if (meters >= 1000) {
      return `${(meters / 1000).toFixed(1)} km`;
    }
    return `${meters} m`;
  }

  getAlarmDescription(alarm: ProfileOverviewAlarm, type: string): string {
    switch (type) {
      case 'pokemon': {
        const id = alarm.pokemon_id ?? 0;
        if (id === 0) return this.i18n.instant('PROFILES.ALL_POKEMON');
        const name = this.masterData.getPokemonName(id);
        const form = this.masterData.getFormName(id, alarm.form ?? 0);
        return form ? `${name} (${form})` : name;
      }
      case 'raid': {
        const id = alarm.pokemon_id ?? alarm.raid_pokemon_id ?? 0;
        if (id > 0 && id !== 9000) return this.masterData.getPokemonName(id);
        const level = alarm.level ?? 9000;
        if (level === 9000) return this.i18n.instant('PROFILES.ALL_RAIDS');
        return level === 6 ? this.i18n.instant('PROFILES.ALL_MEGA_RAIDS') : this.i18n.instant('PROFILES.ALL_LEVEL_RAIDS', { level });
      }
      case 'egg': {
        const level = alarm.level ?? 9000;
        if (level === 9000) return this.i18n.instant('PROFILES.ALL_EGGS');
        return level === 6 ? this.i18n.instant('PROFILES.MEGA_EGG') : this.i18n.instant('PROFILES.LEVEL_EGG', { level });
      }
      case 'quest': {
        const rt = alarm.reward_type ?? 0;
        const id = alarm.pokemon_id ?? alarm.reward ?? 0;
        if (rt === 7 && id > 0) return this.masterData.getPokemonName(id);
        if (rt === 7) return this.i18n.instant('PROFILES.ANY_POKEMON_ENCOUNTER');
        if (rt === 12 && id > 0) return this.i18n.instant('PROFILES.MEGA_ENERGY', { name: this.masterData.getPokemonName(id) });
        if (rt === 4 && id > 0) return this.i18n.instant('PROFILES.CANDY', { name: this.masterData.getPokemonName(id) });
        if (rt === 3)
          return alarm.amount ? `${alarm.amount}+ ${this.i18n.instant('PROFILES.STARDUST')}` : this.i18n.instant('PROFILES.STARDUST');
        if (rt === 2 && (alarm.reward ?? 0) > 0) return this.masterData.getItemName(alarm.reward!);
        if (rt === 2) return this.i18n.instant('PROFILES.ITEM_REWARD');
        return this.i18n.instant('PROFILES.QUEST_REWARD');
      }
      case 'invasion':
        return getGruntDisplayName(alarm.grunt_type ?? null, alarm.gender, key => this.i18n.instant(key));
      case 'lure':
        return this.getLureName(alarm.lure_id ?? 0);
      case 'nest': {
        const id = alarm.pokemon_id ?? 0;
        if (id === 0) return this.i18n.instant('PROFILES.ANY_NEST');
        const name = this.masterData.getPokemonName(id);
        return this.i18n.instant('PROFILES.POKEMON_NEST', { name });
      }
      case 'gym': {
        const team = alarm.team ?? 0;
        if (alarm.slot_changes || alarm.battle_changes) return this.i18n.instant('PROFILES.GYM_ACTIVITY');
        if (team === 0) return this.i18n.instant('PROFILES.ANY_TEAM');
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
        return this.i18n.instant('PROFILES.ALARM');
    }
  }

  getAlarmImage(alarm: ProfileOverviewAlarm, type: string): string {
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

  getAlarmKey(alarm: ProfileOverviewAlarm, type: string): string {
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

  getDuplicateProfiles(alarm: ProfileOverviewAlarm, type: string): string[] {
    const data = this.overview();
    if (!data) return [];

    const key = this.getAlarmKey(alarm, type);
    const alarms = (data[type as keyof ProfileOverview] as ProfileOverviewAlarm[] | undefined) ?? [];
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
        if (!backup.version || !backup.profileName || typeof backup.alarms !== 'object') {
          this.snackBar.open(this.i18n.instant('PROFILES.SNACK_INVALID_BACKUP'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          return;
        }
        if (backup.version !== 1) {
          this.snackBar.open(this.i18n.instant('PROFILES.SNACK_UNSUPPORTED_VERSION'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
          return;
        }

        const ref = this.dialog.open(ConfirmDialogComponent, {
          width: '400px',
          data: {
            confirmText: this.i18n.instant('PROFILES.IMPORT'),
            message: this.i18n.instant('PROFILES.IMPORT_DESC'),
            promptField: {
              existingNames: this.managedProfiles().map(p => p.name),
              label: this.i18n.instant('PROFILES.PROFILE_NAME'),
              value: backup.profileName,
            },
            title: this.i18n.instant('PROFILES.IMPORT_DIALOG_TITLE'),
          } as ConfirmDialogData,
        });
        ref.afterClosed().subscribe((name: string | false) => {
          if (name) {
            this.switching.set(true);
            this.profileOverviewService
              .importProfile({ ...backup, profileName: name })
              .pipe(takeUntilDestroyed(this.destroyRef))
              .subscribe({
                error: () => {
                  this.switching.set(false);
                  this.snackBar.open(this.i18n.instant('PROFILES.SNACK_FAILED_IMPORT'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
                },
                next: res => {
                  this.switching.set(false);
                  if (res.token) this.authService.setToken(res.token);
                  this.snackBar.open(
                    this.i18n.instant('PROFILES.SNACK_IMPORTED', { count: res.alarmsCopied }),
                    this.i18n.instant('TOAST.OK'),
                    { duration: 3000 },
                  );
                  this.loadAll();
                },
              });
          }
        });
      } catch {
        this.snackBar.open(this.i18n.instant('PROFILES.SNACK_INVALID_BACKUP'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
      }
    };
    reader.readAsText(file);
  }

  isActiveProfile(profileNo: number): boolean {
    return this.activeProfileNo() === profileNo;
  }

  isDuplicate(alarm: ProfileOverviewAlarm): boolean {
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

  switchProfile(profile: ProfileOverviewProfile): void {
    this.switching.set(true);
    this.profileService
      .switchProfile(profile.profile_no)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.switching.set(false);
          this.snackBar.open(this.i18n.instant('PROFILES.SNACK_FAILED_SWITCH'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        },
        next: res => {
          this.switching.set(false);
          if (res.token) {
            this.authService.setToken(res.token);
          }
          this.snackBar.open(this.i18n.instant('PROFILES.SNACK_SWITCHED', { name: profile.name }), this.i18n.instant('TOAST.OK'), {
            duration: 3000,
          });
          this.authService.loadCurrentUser();
          this.loadAll();
        },
      });
  }

  toggleDuplicatesFilter(): void {
    this.showDuplicatesOnly.update(v => !v);
  }

  private alarmMatchesSearch(alarm: ProfileOverviewAlarm, type: string, search: string): boolean {
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
      const labels: Record<string, string> = {
        name: this.i18n.instant('FORT_CHANGES.LABEL_NAME'),
        image_url: this.i18n.instant('FORT_CHANGES.LABEL_IMAGE'),
        location: this.i18n.instant('FORT_CHANGES.LABEL_LOCATION'),
        new: this.i18n.instant('FORT_CHANGES.LABEL_NEW'),
        removal: this.i18n.instant('FORT_CHANGES.LABEL_REMOVAL'),
      };
      return types.map(t => labels[t] ?? t).join(', ');
    } catch {
      return raw;
    }
  }

  private formatFortType(type: string | null): string {
    switch (type) {
      case 'pokestop':
        return this.i18n.instant('PROFILES.POKESTOP_CHANGES');
      case 'gym':
        return this.i18n.instant('PROFILES.GYM_CHANGES');
      default:
        return this.i18n.instant('PROFILES.ALL_FORT_CHANGES');
    }
  }

  private getLureName(id: number): string {
    switch (id) {
      case 0:
        return this.i18n.instant('PROFILES.ALL_LURES');
      case 501:
        return this.i18n.instant('PROFILES.LURE_NORMAL');
      case 502:
        return this.i18n.instant('PROFILES.LURE_GLACIAL');
      case 503:
        return this.i18n.instant('PROFILES.LURE_MOSSY');
      case 504:
        return this.i18n.instant('PROFILES.LURE_MAGNETIC');
      case 505:
        return this.i18n.instant('PROFILES.LURE_RAINY');
      case 506:
        return this.i18n.instant('PROFILES.LURE_GOLDEN');
      default:
        return this.i18n.instant('PROFILES.LURE_NUM', { id });
    }
  }

  private getMaxBattleLabel(level: number): string {
    switch (level) {
      case 1:
      case 2:
      case 3:
      case 4:
      case 5:
        return this.i18n.instant('PROFILES.STAR_MAX_BATTLE', { stars: level });
      case 7:
        return this.i18n.instant('PROFILES.GIGANTAMAX');
      case 8:
        return this.i18n.instant('PROFILES.LEGENDARY_GIGANTAMAX');
      default:
        return this.i18n.instant('PROFILES.ANY_MAX_BATTLE');
    }
  }

  private getTeamName(team: number): string {
    switch (team) {
      case 1:
        return this.i18n.instant('PROFILES.TEAM_MYSTIC');
      case 2:
        return this.i18n.instant('PROFILES.TEAM_VALOR');
      case 3:
        return this.i18n.instant('PROFILES.TEAM_INSTINCT');
      default:
        return this.i18n.instant('PROFILES.TEAM_NUM', { team });
    }
  }

  private loadAll(): void {
    this.loading.set(true);
    forkJoin([this.profileService.getAll(), this.profileOverviewService.getOverview()])
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.loading.set(false);
          this.snackBar.open(this.i18n.instant('PROFILES.SNACK_FAILED_LOAD'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        },
        next: ([profiles, data]) => {
          this.managedProfiles.set(profiles);
          const active = profiles.find(p => p.active);
          if (active) this.activeProfileNo.set(active.profileNo);
          this.overview.set(data);
          this.loading.set(false);
          this.expandedProfiles.set(new Set());
        },
      });
  }
}
