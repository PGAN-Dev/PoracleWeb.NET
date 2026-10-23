import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  inject,
  signal,
  computed,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, FormBuilder, FormsModule, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialog, MatDialogModule, MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatSortModule, Sort } from '@angular/material/sort';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { forkJoin } from 'rxjs';

import { AdminListState, StatusFilter, notesLabel } from './admin-list-state';
import { AdminUser } from '../../core/models';
import { AdminService, CreateDestinationRequest } from '../../core/services/admin.service';
import { AuthService } from '../../core/services/auth.service';
import { I18nService } from '../../core/services/i18n.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';
import { DESTINATION_KINDS, isKnownKind, isPersonType, kindIcon, kindLabelKey } from '../../shared/utils/human-kind';
import { relativeTime } from '../../shared/utils/relative-time';
import { serverReason } from '../../shared/utils/server-reason';

/**
 * A webhook URL with its secret token cut off: the host and, for a Discord-style URL, the webhook number.
 * Anything after that number, and the whole path of any other URL, is dropped rather than shortened,
 * since the token is usually the last segment. The full URL stays reachable only through Copy ID.
 */
export function shortWebhookUrl(url: string): string {
  let parsed: URL;
  try {
    parsed = new URL(/^[a-z][a-z0-9+.-]*:\/\//i.test(url) ? url : `https://${url}`);
  } catch {
    return '…';
  }
  if (!parsed.host || /\s/.test(url)) return '…';
  const segments = parsed.pathname.split('/').filter(Boolean);
  const hooks = segments.findIndex(s => s.toLowerCase() === 'webhooks');
  if (hooks >= 0 && /^\d+$/.test(segments[hooks + 1] ?? '')) {
    return `${parsed.host}/${segments.slice(0, hooks + 2).join('/')}…`;
  }
  return segments.length ? `${parsed.host}/…` : parsed.host;
}

/** What to call a row in a message: its name, else its id, except that a webhook id is a secret and is shortened. */
export function destinationName(row: AdminUser): string {
  if (row.name) return row.name;
  return (row.type ?? '').trim().toLowerCase() === 'webhook' ? shortWebhookUrl(row.id) : row.id;
}

// ─── Add Destination Dialog ───────────────────────────────────────────────────────

// The id rules mirror HumanKind.ValidateNewId on the server, so a typo is caught here rather than as a 400.
// They refuse only what cannot be real: nothing checks that the channel or chat exists.
const WEBHOOK_URL = /^https?:\/\/\P{Cc}+$/iu;
const DISCORD_ID = /^[0-9]{1,20}$/;
const TELEGRAM_CHAT_ID = /^-[0-9]{1,19}$/;
const TELEGRAM_TOPIC_NUMBER = /^[1-9][0-9]{0,9}$/;

/** A pattern checked against the trimmed value, answering `{ [error]: true }` on a miss. Empty is `required`'s. */
function trimmedPattern(pattern: RegExp, error: string): ValidatorFn {
  return (control: AbstractControl): ValidationErrors | null => {
    const value = String(control.value ?? '').trim();
    return !value || pattern.test(value) ? null : { [error]: true };
  };
}

const ID_VALIDATORS: ReadonlyMap<string, ValidatorFn> = new Map([
  ['discord:channel', trimmedPattern(DISCORD_ID, 'digits')],
  ['discord:thread', trimmedPattern(DISCORD_ID, 'digits')],
  ['telegram:channel', trimmedPattern(TELEGRAM_CHAT_ID, 'negativeChat')],
  ['telegram:group', trimmedPattern(TELEGRAM_CHAT_ID, 'negativeChat')],
  ['webhook', trimmedPattern(WEBHOOK_URL, 'url')],
]);

/** The kind picker, one row per platform. */
const KIND_GROUPS: readonly (readonly string[])[] = [
  DESTINATION_KINDS.filter(k => k === 'webhook'),
  DESTINATION_KINDS.filter(k => k.startsWith('discord:')),
  DESTINATION_KINDS.filter(k => k.startsWith('telegram:')),
];

/**
 * Asks what kind of destination first, then for the id that kind is addressed by: a URL for a webhook, a
 * snowflake for a Discord channel or thread, a negative chat id for Telegram, and for a topic the chat id
 * and the topic number, joined as `chat:topic`. Saves the destination itself and closes with the request
 * only once the server has accepted it: a refusal keeps everything typed and shows the reason on the id
 * field (the chat field for a topic), where it can be corrected. See #1025.
 */
@Component({
  imports: [
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatRadioModule,
    TranslatePipe,
  ],
  selector: 'app-add-destination-dialog',
  standalone: true,
  styleUrl: './add-destination-dialog.component.scss',
  templateUrl: './add-destination-dialog.component.html',
})
export class AddDestinationDialogComponent {
  private readonly adminService = inject(AdminService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(FormBuilder);
  private readonly i18n = inject(I18nService);
  readonly dialogRef = inject(MatDialogRef<AddDestinationDialogComponent>);

  readonly form = this.fb.nonNullable.group({
    id: [{ disabled: true, value: '' }, Validators.required],
    name: ['', Validators.required],
    chatId: [{ disabled: true, value: '' }, [Validators.required, trimmedPattern(TELEGRAM_CHAT_ID, 'negativeChat')]],
    kind: ['', Validators.required],
    notes: [''],
    topicId: [{ disabled: true, value: '' }, [Validators.required, trimmedPattern(TELEGRAM_TOPIC_NUMBER, 'topicPositive')]],
  });

  readonly kind = signal('');
  readonly kindGroups = KIND_GROUPS;
  readonly kindIcon = kindIcon;
  readonly kindLabelKey = kindLabelKey;
  readonly saving = signal(false);

  constructor() {
    const { chatId, kind, topicId } = this.form.controls;
    kind.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(k => this.onKindChange(k));
    // A topic's refusal sits on the chat field but is about the pair, so editing the topic number lifts it too.
    topicId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      if (chatId.hasError('server')) chatId.updateValueAndValidity();
    });
  }

  /** The request the dialog closes with, or null while the form is incomplete. */
  request(): CreateDestinationRequest | null {
    if (this.form.invalid) return null;
    const v = this.form.getRawValue();
    const id = v.kind === 'telegram:topic' ? `${v.chatId.trim()}:${v.topicId.trim()}` : v.id.trim();
    const notes = v.notes.trim();
    return { id, name: v.name.trim(), type: v.kind, ...(notes ? { notes } : {}) };
  }

  submit(): void {
    if (this.saving()) return;
    const req = this.request();
    if (!req) return;

    this.saving.set(true);
    this.dialogRef.disableClose = true;
    this.adminService
      .createDestination(req)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: err => {
          this.saving.set(false);
          this.dialogRef.disableClose = false;
          // Any edit re-runs the field's validators, which drops this error, so a corrected id can be sent.
          const field = req.type === 'telegram:topic' ? this.form.controls.chatId : this.form.controls.id;
          field.setErrors({ ...field.errors, server: this.failureMessage(err) });
          field.markAsTouched();
        },
        next: () => this.dialogRef.close(req),
      });
  }

  /**
   * What to tell the admin when a create fails. A 409 names the row already holding the id, so an admin who
   * pasted the wrong channel can tell it from one they added earlier; the server leaves the name and type
   * out when it could not read that row, and then its own sentence is shown. A person holding the id is
   * labelled as one, not as "Other". 502 is the #482 half-write
   * answer ("Check the destination list before trying again"), which must reach the admin: a retry on the
   * generic message can create a second row.
   */
  private failureMessage(err: unknown): string {
    const failure = err as { error?: { name?: unknown; type?: unknown }; status?: unknown } | null | undefined;
    const holder = failure?.error;
    // The name is the signal that the server read the holder; a person's type can legitimately be null.
    if (failure?.status === 409 && typeof holder?.name === 'string' && (typeof holder.type === 'string' || holder.type == null)) {
      const type = typeof holder.type === 'string' ? holder.type : null;
      const typeKey = isPersonType(type) ? 'ADMIN.TYPE_PERSON' : kindLabelKey(type);
      return this.i18n.instant('ADMIN.DESTINATION_EXISTS', { name: holder.name, type: this.i18n.instant(typeKey) });
    }
    return serverReason(err, { allow5xx: [502] }) ?? this.i18n.instant('ADMIN.SNACK_FAILED_ADD_DESTINATION');
  }

  /** The name is kept; every id field is cleared, since an id for one kind is never right for another. */
  private onKindChange(kind: string): void {
    this.kind.set(kind);
    const { id, chatId, topicId } = this.form.controls;
    for (const control of [chatId, id, topicId]) {
      control.reset('', { emitEvent: false });
      control.disable({ emitEvent: false });
    }

    const idValidator = ID_VALIDATORS.get(kind);
    if (kind === 'telegram:topic') {
      chatId.enable({ emitEvent: false });
      topicId.enable({ emitEvent: false });
    } else if (idValidator) {
      id.setValidators([Validators.required, idValidator]);
      id.enable({ emitEvent: false });
    }

    this.form.updateValueAndValidity();
  }
}

// ─── Destination Delegates Dialog ─────────────────────────────────────────────────

interface DelegatesDialogData {
  allUsers: AdminUser[];
  porocleDelegates: string[];
  webhook: AdminUser;
}

@Component({
  imports: [
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatChipsModule,
    MatProgressSpinnerModule,
    MatAutocompleteModule,
    MatTooltipModule,
    TranslatePipe,
  ],
  selector: 'app-destination-delegates-dialog',
  standalone: true,
  styleUrl: './destination-delegates-dialog.component.scss',
  templateUrl: './destination-delegates-dialog.component.html',
})
export class DestinationDelegatesDialogComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  private readonly i18n = inject(I18nService);
  private readonly snackBar = inject(MatSnackBar);
  readonly data = inject<DelegatesDialogData>(MAT_DIALOG_DATA);
  readonly delegates = signal<string[]>([]);

  readonly dialogRef = inject(MatDialogRef<DestinationDelegatesDialogComponent>);
  readonly filteredUsers = signal<AdminUser[]>([]);
  readonly loading = signal(true);
  searchText = '';
  readonly webhookName = destinationName(this.data.webhook);

  /**
   * The user the field names, once it names one: an option picked from the list fills in its id. Free
   * text ("Sam") used to be sent as an id and came back as a 400 behind a generic toast.
   */
  get pickedUserId(): string | null {
    const text = this.searchText.trim();
    return this.users.find(u => u.id === text)?.id ?? null;
  }

  /** People only: a channel, a group or an unknown type is nobody who could sign in and use the grant. */
  private get users(): AdminUser[] {
    return this.data.allUsers.filter(u => u.kind === 'person');
  }

  addDelegate(userId: string): void {
    this.adminService.addWebhookDelegate(this.data.webhook.id, userId).subscribe({
      error: err =>
        this.snackBar.open(serverReason(err) ?? this.i18n.instant('ADMIN.SNACK_FAILED_ADD_DELEGATE'), this.i18n.instant('TOAST.OK'), {
          duration: 4000,
        }),
      next: d => {
        this.delegates.set(d);
        this.searchText = '';
        this.filteredUsers.set([]);
      },
    });
  }

  addDelegateFromSearch(event?: Event): void {
    // Enter on a highlighted option is the autocomplete's: it selects the option (which adds it) and
    // marks the key handled, so adding here as well would send the request twice.
    if (event?.defaultPrevented) return;
    const uid = this.pickedUserId;
    if (!uid) return;
    this.addDelegate(uid);
  }

  getAvatarUrl(userId: string): string | null {
    return this.users.find(u => u.id === userId)?.avatarUrl ?? null;
  }

  getDisplayName(userId: string): string {
    const user = this.users.find(u => u.id === userId);
    return user?.name ? `${user.name} (${userId})` : userId;
  }

  ngOnInit(): void {
    this.adminService.getWebhookDelegates(this.data.webhook.id).subscribe({
      error: () => {
        this.loading.set(false);
        this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_LOAD_DELEGATES'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
      },
      next: d => {
        this.delegates.set(d);
        this.loading.set(false);
      },
    });
  }

  onSearchChange(term: string): void {
    const t = term.toLowerCase().trim();
    if (!t) {
      this.filteredUsers.set([]);
      return;
    }
    this.filteredUsers.set(this.users.filter(u => u.id.includes(t) || (u.name || '').toLowerCase().includes(t)).slice(0, 10));
  }

  onUserSelected(userId: string): void {
    this.searchText = userId;
    this.filteredUsers.set([]);
    this.addDelegate(userId);
  }

  removeDelegate(userId: string): void {
    this.adminService.removeWebhookDelegate(this.data.webhook.id, userId).subscribe({
      error: err =>
        this.snackBar.open(serverReason(err) ?? this.i18n.instant('ADMIN.SNACK_FAILED_REMOVE_DELEGATE'), this.i18n.instant('TOAST.OK'), {
          duration: 4000,
        }),
      next: d => this.delegates.set(d),
    });
  }
}

// ─── Destinations Page ────────────────────────────────────────────────────────────

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    MatCardModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatInputModule,
    MatFormFieldModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatDialogModule,
    MatDividerModule,
    MatMenuModule,
    MatTooltipModule,
    MatPaginatorModule,
    MatSortModule,
    MatSelectModule,
    MatChipsModule,
    TranslatePipe,
  ],
  selector: 'app-admin-destinations',
  standalone: true,
  styleUrl: './admin-destinations.component.scss',
  templateUrl: './admin-destinations.component.html',
})
export class AdminDestinationsComponent implements OnInit {
  private readonly adminService = inject(AdminService);
  private readonly allUsers = signal<AdminUser[]>([]);
  private readonly auth = inject(AuthService);
  // Every row the server calls a destination, unknown types included, so this page and Users together
  // account for every account. See #1025.
  private readonly destinations = computed(() => this.allUsers().filter(u => u.kind === 'destination'));
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  private readonly i18n = inject(I18nService);
  private readonly injector = inject(Injector);
  private readonly poracleAdmins = signal<string[]>([]);
  private readonly porocleDelegates = signal<Record<string, string[]>>({});
  private readonly snackBar = inject(MatSnackBar);
  private readonly webhookDelegates = signal<Record<string, string[]>>({});

  readonly columns = ['name', 'type', 'id', 'enabled', 'disabledSince', 'delegates', 'actions'];

  readonly isKnownKind = isKnownKind;
  /** The id of the destination just added, so the table can turn to its page and mark it. */
  readonly justAdded = signal<string | null>(null);
  readonly kindIcon = kindIcon;
  readonly kindLabelKey = kindLabelKey;

  readonly list = new AdminListState(this.destinations, 'admin_destinations_', (row, column) => this.getSortValue(row, column));

  readonly usersLoading = signal(true);

  /** The Block button's tooltip: a channel or group can be blocked by Poracle too, which is worth knowing here. */
  blockTooltipKey(row: AdminUser): string {
    return this.isWebhook(row) ? 'ADMIN.BLOCK_DESTINATION' : 'ADMIN.BLOCKED_CHANNEL_TOOLTIP';
  }

  copyId(id: string): void {
    navigator.clipboard.writeText(id).then(() => {
      this.snackBar.open(this.i18n.instant('ADMIN.SNACK_ID_COPIED'), undefined, { duration: 2000 });
    });
  }

  deleteAlarms(user: AdminUser): void {
    const name = this.rowName(user);
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('COMMON.DELETE_ALL'),
        message: this.i18n.instant('ADMIN.CONFIRM_DELETE_ALARMS', { name }),
        title: this.i18n.instant('ADMIN.DELETE_ALL_ALARMS'),
        warn: true,
      } as ConfirmDialogData,
    });

    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.adminService.deleteUserAlarms(user.id).subscribe({
          error: err => this.showFailure(err, 'ADMIN.SNACK_FAILED_ALARMS'),
          next: result => {
            this.snackBar.open(
              this.i18n.instant('ADMIN.SNACK_ALARMS_DELETED', { name, count: result.deleted }),
              this.i18n.instant('TOAST.OK'),
              { duration: 3000 },
            );
          },
        });
      }
    });
  }

  deleteDestination(user: AdminUser): void {
    const name = this.rowName(user);
    let message = this.i18n.instant('ADMIN.CONFIRM_DELETE_DESTINATION', { name });
    // Deleting the row never touches the channel or group it posts into; say which one that is.
    if (!this.isWebhook(user)) {
      message +=
        ' ' +
        (isKnownKind(user.type)
          ? this.i18n.instant('ADMIN.DELETE_DESTINATION_KIND_NOTE', { kind: this.i18n.instant(kindLabelKey(user.type)) })
          : this.i18n.instant('ADMIN.DELETE_DESTINATION_NOTE'));
    }
    const ref = this.dialog.open(ConfirmDialogComponent, {
      data: {
        confirmText: this.i18n.instant('ADMIN.DELETE_DESTINATION'),
        message,
        title: this.i18n.instant('ADMIN.DELETE_DESTINATION'),
        warn: true,
      } as ConfirmDialogData,
    });

    ref.afterClosed().subscribe(confirmed => {
      if (confirmed) {
        this.adminService.deleteUser(user.id).subscribe({
          error: err => this.showFailure(err, 'ADMIN.SNACK_FAILED_DELETE_DESTINATION'),
          next: () => {
            this.allUsers.update(users => users.filter(u => u.id !== user.id));
            this.snackBar.open(this.i18n.instant('ADMIN.SNACK_DESTINATION_DELETED', { name }), this.i18n.instant('TOAST.OK'), {
              duration: 3000,
            });
          },
        });
      }
    });
  }

  formatDate(dateStr: string): string {
    const d = new Date(dateStr);
    return isNaN(d.getTime()) ? '' : d.toLocaleString();
  }

  getDelegateAvatarUrl(userId: string): string | null {
    return this.allUsers().find(u => u.id === userId)?.avatarUrl ?? null;
  }

  getDelegateName(userId: string): string {
    const user = this.allUsers().find(u => u.id === userId);
    return user?.name ? `${user.name} (${userId})` : userId;
  }

  /**
   * The real grants on a webhook: Poracle config delegates and the ones added here. Global admins can
   * manage every webhook, which the page header says once; listing them on every row buried the grants
   * someone actually made under the same avatars on every hook.
   */
  getDelegateUserIds(webhookId: string): string[] {
    const db = this.webhookDelegates()[webhookId] ?? [];
    const configDelegates = this.porocleDelegates()[webhookId] ?? [];
    return [...new Set([...configDelegates, ...db])];
  }

  isLockedDelegate(webhookId: string, userId: string): boolean {
    return this.isPoracleAdmin(userId) || this.isPorocleDelegateForWebhook(webhookId, userId);
  }

  /** Returns true if this userId is locked (from Poracle config — not DB-managed). */
  isPoracleAdmin(userId: string): boolean {
    return this.poracleAdmins().includes(userId);
  }

  isPorocleDelegateForWebhook(webhookId: string, userId: string): boolean {
    return (this.porocleDelegates()[webhookId] ?? []).includes(userId);
  }

  /** Grants and the delegates dialog are webhook-only on the server until delegation widens (#1025 part 6). */
  isWebhook(row: AdminUser): boolean {
    return (row.type ?? '').trim().toLowerCase() === 'webhook';
  }

  loadUsers(): void {
    this.usersLoading.set(true);
    forkJoin({
      delegates: this.adminService.getAllWebhookDelegates(),
      poracleAdmins: this.adminService.getPoracleAdmins(),
      porocleDelegates: this.adminService.getPorocleDelegates(),
      users: this.adminService.getUsers(),
    })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.usersLoading.set(false);
          this.snackBar.open(this.i18n.instant('ADMIN.SNACK_FAILED_LOAD_WEBHOOKS'), this.i18n.instant('TOAST.OK'), { duration: 3000 });
        },
        next: ({ delegates, poracleAdmins, porocleDelegates, users }) => {
          this.allUsers.set(users);
          this.webhookDelegates.set(delegates);
          this.poracleAdmins.set(poracleAdmins);
          this.porocleDelegates.set(porocleDelegates);
          this.usersLoading.set(false);
          this.revealJustAdded();
        },
      });
  }

  manageAlarms(user: AdminUser): void {
    this.adminService
      .impersonateById(user.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: err =>
          this.snackBar.open(serverReason(err) ?? this.i18n.instant('ADMIN.SNACK_FAILED_SWITCH_WEBHOOK'), this.i18n.instant('TOAST.OK'), {
            duration: 4000,
          }),
        next: res => this.auth.impersonate(res.token),
      });
  }

  ngOnInit(): void {
    this.loadUsers();
  }

  notesLabel(notes: string | null): string | null {
    return notesLabel(notes);
  }

  onPageChange(event: PageEvent): void {
    this.list.setPage(event.pageIndex, event.pageSize);
  }

  onSearchChange(term: string): void {
    this.list.searchTerm.set(term);
    this.list.resetPage();
  }

  onSortChange(sort: Sort): void {
    this.list.sortActive.set(sort.active);
    this.list.sortDirection.set(sort.direction);
    this.list.resetPage();
  }

  onStatusFilterChange(status: StatusFilter): void {
    this.list.statusFilter.set(status);
    this.list.resetPage();
  }

  onTypeFilterChange(type: string | null | undefined): void {
    // A listbox chip clicked a second time deselects to undefined; that means "all", not nothing.
    this.list.typeFilter.set(type || 'all');
    this.list.resetPage();
  }

  /** The dialog saves the destination itself and closes with it only once the server has accepted it. */
  openAddDialog(): void {
    const ref = this.dialog.open<AddDestinationDialogComponent, unknown, CreateDestinationRequest | null>(AddDestinationDialogComponent, {
      width: '520px',
    });
    ref.afterClosed().subscribe(created => {
      if (!created) return;
      this.justAdded.set(created.id);
      this.snackBar.open(this.i18n.instant('ADMIN.SNACK_DESTINATION_ADDED', { name: created.name }), this.i18n.instant('TOAST.OK'), {
        duration: 3000,
      });
      this.loadUsers();
    });
  }

  openDelegatesDialog(webhook: AdminUser): void {
    const ref = this.dialog.open(DestinationDelegatesDialogComponent, {
      width: '520px',
      data: {
        allUsers: this.allUsers(),
        porocleDelegates: this.porocleDelegates()[webhook.id] ?? [],
        webhook,
      } satisfies DelegatesDialogData,
    });
    ref.afterClosed().subscribe(() => {
      forkJoin({
        delegates: this.adminService.getAllWebhookDelegates(),
        porocleDelegates: this.adminService.getPorocleDelegates(),
      }).subscribe({
        next: ({ delegates, porocleDelegates }) => {
          this.webhookDelegates.set(delegates);
          this.porocleDelegates.set(porocleDelegates);
        },
      });
    });
  }

  pauseWebhook(user: AdminUser): void {
    this.adminService
      .pauseUser(user.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: err => this.showFailure(err, 'ADMIN.SNACK_FAILED_STOP'),
        next: updated => {
          this.allUsers.update(users => users.map(u => (u.id === user.id ? { ...u, enabled: updated.enabled } : u)));
          this.snackBar.open(this.i18n.instant('ADMIN.SNACK_STOPPED', { name: this.rowName(user) }), this.i18n.instant('TOAST.OK'), {
            duration: 3000,
          });
        },
      });
  }

  relativeDate(dateStr: string): string {
    const then = new Date(dateStr).getTime();
    return isNaN(then) ? '—' : relativeTime(then, Date.now(), this.i18n.currentLang());
  }

  resumeWebhook(user: AdminUser): void {
    this.adminService
      .resumeUser(user.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: err => this.showFailure(err, 'ADMIN.SNACK_FAILED_START'),
        next: updated => {
          this.allUsers.update(users => users.map(u => (u.id === user.id ? { ...u, enabled: updated.enabled } : u)));
          this.snackBar.open(this.i18n.instant('ADMIN.SNACK_STARTED', { name: this.rowName(user) }), this.i18n.instant('TOAST.OK'), {
            duration: 3000,
          });
        },
      });
  }

  /** A row's name for messages and labels, never a webhook's token. */
  rowName(row: AdminUser): string {
    return destinationName(row);
  }

  toggleBlock(user: AdminUser, unblock: boolean): void {
    const action$ = unblock ? this.adminService.enableUser(user.id) : this.adminService.disableUser(user.id);

    action$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      error: err => this.showFailure(err, 'ADMIN.SNACK_FAILED_BLOCK'),
      next: updated => {
        this.allUsers.update(users => users.map(u => (u.id === user.id ? { ...u, adminDisable: updated.adminDisable } : u)));
        // Users' "User blocked" named nobody, and a destination is not a user.
        const name = this.rowName(user);
        this.snackBar.open(
          this.i18n.instant(unblock ? 'ADMIN.SNACK_DESTINATION_UNBLOCKED' : 'ADMIN.SNACK_DESTINATION_BLOCKED', { name }),
          this.i18n.instant('TOAST.OK'),
          { duration: 3000 },
        );
      },
    });
  }

  /** The ID cell for a webhook. This used to keep the last 20 characters, which is exactly the token. */
  truncateUrl(url: string): string {
    return shortWebhookUrl(url);
  }

  private getSortValue(row: AdminUser, column: string): string | number {
    switch (column) {
      case 'id':
        return row.id;
      case 'name':
        return (row.name || '').toLowerCase();
      case 'type':
        return (row.type ?? '').trim().toLowerCase();
      case 'enabled':
        return row.adminDisable === 1 ? 2 : row.enabled === 0 ? 1 : 0;
      case 'disabledSince':
        return row.disabledDate ? new Date(row.disabledDate).getTime() : 0;
      default:
        return '';
    }
  }

  /**
   * Turns to the page holding the destination just added and scrolls its row into view, since a full page
   * runs past the fold. A search or filter that hides it is left alone. No glide for reduced motion.
   */
  private revealJustAdded(): void {
    const added = this.justAdded();
    if (!added) return;
    const index = this.list.filtered().findIndex(row => row.id === added);
    if (index < 0) return;
    this.list.setPage(Math.floor(index / this.list.pageSize()), this.list.pageSize());
    afterNextRender(
      () => {
        const row = this.host.nativeElement.querySelector('tr.just-added');
        const reduce = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false;
        // Centred, not nearest: at the bottom edge the snackbar announcing it sits on top of the row.
        row?.scrollIntoView?.({ behavior: reduce ? 'auto' : 'smooth', block: 'center' });
      },
      { injector: this.injector },
    );
  }

  /** The server's own reason when it gave one, else the generic message. */
  private showFailure(err: unknown, fallbackKey: string): void {
    this.snackBar.open(serverReason(err) ?? this.i18n.instant(fallbackKey), this.i18n.instant('TOAST.OK'), { duration: 4000 });
  }
}
