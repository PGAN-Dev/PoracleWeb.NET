import { HttpClient } from '@angular/common/http';
import { Component, Input, ElementRef, OnInit, OnDestroy, inject, ChangeDetectorRef } from '@angular/core';

import { ConfigService } from '../../../core/services/config.service';

@Component({
  selector: 'app-discord-avatar',
  standalone: true,
  styleUrl: './discord-avatar.component.scss',
  templateUrl: './discord-avatar.component.html',
})
export class DiscordAvatarComponent implements OnInit, OnDestroy {
  private static batchTimer: ReturnType<typeof setTimeout> | null = null;
  private static cache = new Map<string, string>();
  private static config: ConfigService;

  private static http: HttpClient;

  private static pending = new Map<string, Set<DiscordAvatarComponent>>();
  private cdr = inject(ChangeDetectorRef);
  private configSvc = inject(ConfigService);
  private el = inject(ElementRef);
  private httpClient = inject(HttpClient);

  private observer: IntersectionObserver | null = null;
  @Input() defaultUrl = 'https://cdn.discordapp.com/embed/avatars/0.png';
  resolvedUrl = '';
  @Input() userId = '';
  @Input() userType = '';

  private static flushBatch(): void {
    const ids = [...this.pending.keys()];
    if (ids.length === 0) return;

    const pendingSnapshot = new Map(this.pending);
    this.pending.clear();
    this.batchTimer = null;

    this.http.post<Record<string, string>>(`${this.config.apiHost}/api/admin/users/avatars`, ids).subscribe({
      next: avatarMap => {
        for (const [id, url] of Object.entries(avatarMap)) {
          this.cache.set(id, url);
          pendingSnapshot.get(id)?.forEach(comp => {
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

  ngOnDestroy(): void {
    this.observer?.disconnect();
    // Remove from pending
    DiscordAvatarComponent.pending.get(this.userId)?.delete(this);
  }

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
      entries => {
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
}
