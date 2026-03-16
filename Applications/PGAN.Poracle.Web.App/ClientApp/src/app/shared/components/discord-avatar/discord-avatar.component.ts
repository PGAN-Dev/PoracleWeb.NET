import { Component, Input, ElementRef, OnInit, OnDestroy, inject, ChangeDetectorRef } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ConfigService } from '../../../core/services/config.service';

@Component({
  selector: 'app-discord-avatar',
  standalone: true,
  template: `<img [src]="resolvedUrl" class="avatar-img" (error)="onError($event)" loading="lazy" />`,
  styles: [`
    .avatar-img {
      width: 32px;
      height: 32px;
      border-radius: 50%;
      object-fit: cover;
      flex-shrink: 0;
      background: #e0e0e0;
    }
  `],
})
export class DiscordAvatarComponent implements OnInit, OnDestroy {
  @Input() userId = '';
  @Input() defaultUrl = 'https://cdn.discordapp.com/embed/avatars/0.png';
  @Input() userType = '';

  resolvedUrl = '';

  private static cache = new Map<string, string>();
  private static pending = new Map<string, Set<DiscordAvatarComponent>>();
  private static batchTimer: ReturnType<typeof setTimeout> | null = null;
  private static http: HttpClient;
  private static config: ConfigService;

  private el = inject(ElementRef);
  private cdr = inject(ChangeDetectorRef);
  private httpClient = inject(HttpClient);
  private configSvc = inject(ConfigService);
  private observer: IntersectionObserver | null = null;

  ngOnInit(): void {
    DiscordAvatarComponent.http = this.httpClient;
    DiscordAvatarComponent.config = this.configSvc;
    this.resolvedUrl = this.defaultUrl;

    // Already cached?
    if (DiscordAvatarComponent.cache.has(this.userId)) {
      this.resolvedUrl = DiscordAvatarComponent.cache.get(this.userId)!;
      return;
    }

    if (!this.userType?.startsWith('discord')) return;

    // Observe visibility
    this.observer = new IntersectionObserver(
      (entries) => {
        if (entries[0]?.isIntersecting) {
          this.observer?.disconnect();
          this.observer = null;
          this.requestAvatar();
        }
      },
      { rootMargin: '200px' },
    );
    this.observer.observe(this.el.nativeElement);
  }

  ngOnDestroy(): void {
    this.observer?.disconnect();
    // Remove from pending
    DiscordAvatarComponent.pending.get(this.userId)?.delete(this);
  }

  onError(event: Event): void {
    (event.target as HTMLImageElement).src = 'https://cdn.discordapp.com/embed/avatars/0.png';
  }

  private requestAvatar(): void {
    // Check cache again
    if (DiscordAvatarComponent.cache.has(this.userId)) {
      this.resolvedUrl = DiscordAvatarComponent.cache.get(this.userId)!;
      this.cdr.detectChanges();
      return;
    }

    // Add to pending batch
    if (!DiscordAvatarComponent.pending.has(this.userId)) {
      DiscordAvatarComponent.pending.set(this.userId, new Set());
    }
    DiscordAvatarComponent.pending.get(this.userId)!.add(this);

    // Debounce batch request
    if (DiscordAvatarComponent.batchTimer) clearTimeout(DiscordAvatarComponent.batchTimer);
    DiscordAvatarComponent.batchTimer = setTimeout(() => DiscordAvatarComponent.flushBatch(), 200);
  }

  private static flushBatch(): void {
    const ids = [...this.pending.keys()];
    if (ids.length === 0) return;

    const pendingSnapshot = new Map(this.pending);
    this.pending.clear();
    this.batchTimer = null;

    this.http
      .post<Record<string, string>>(`${this.config.apiHost}/api/admin/users/avatars`, ids)
      .subscribe({
        next: (avatarMap) => {
          for (const [id, url] of Object.entries(avatarMap)) {
            this.cache.set(id, url);
            pendingSnapshot.get(id)?.forEach((comp) => {
              comp.resolvedUrl = url;
              comp.cdr.detectChanges();
            });
          }
          // Retry IDs that didn't get a response (rate limited)
          const missed = ids.filter(id => !avatarMap[id] && !this.cache.has(id));
          if (missed.length > 0) {
            setTimeout(() => {
              for (const id of missed) {
                const comps = pendingSnapshot.get(id);
                if (comps) {
                  this.pending.set(id, comps);
                }
              }
              if (this.pending.size > 0) this.flushBatch();
            }, 3000);
          }
        },
      });
  }
}
