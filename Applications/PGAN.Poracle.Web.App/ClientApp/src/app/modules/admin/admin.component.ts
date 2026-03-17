import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-admin',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, MatListModule, MatIconModule],
  template: `
    <div class="page-header">
      <div class="page-header-text">
        <h1>Admin Panel</h1>
        <p class="page-description">Select a section below.</p>
      </div>
    </div>
    <mat-nav-list>
      <a mat-list-item routerLink="/admin/users"><mat-icon>people</mat-icon> Users</a>
      <a mat-list-item routerLink="/admin/webhooks"><mat-icon>webhook</mat-icon> Webhooks</a>
      <a mat-list-item routerLink="/admin/settings"><mat-icon>settings</mat-icon> Settings</a>
    </mat-nav-list>
  `,
  styles: [
    `
      .page-header {
        display: flex;
        justify-content: space-between;
        align-items: flex-start;
        padding: 16px 24px;
        gap: 16px;
      }
      .page-header-text {
        flex: 1;
        min-width: 0;
      }
      .page-header h1 {
        margin: 0;
        font-size: 24px;
        font-weight: 400;
      }
      .page-description {
        margin: 4px 0 0;
        color: var(--text-secondary, rgba(0,0,0,0.54));
        font-size: 13px;
        line-height: 1.5;
        border-left: 3px solid #1976d2;
        padding-left: 12px;
      }
    `,
  ],
})
export class AdminComponent {}
