import { Component, input } from '@angular/core';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { DistanceDisplayPipe } from '../../pipes/distance-display.pipe';

@Component({
  selector: 'app-alarm-info',
  standalone: true,
  imports: [MatChipsModule, MatIconModule, MatTooltipModule, DistanceDisplayPipe],
  template: `
    <div class="alarm-info-row">
      <span class="distance-chip" [class.areas-mode]="distance() === 0">
        <mat-icon class="info-icon">{{ distance() === 0 ? 'map' : 'straighten' }}</mat-icon>
        {{ distance() | distanceDisplay }}
      </span>
      @if (clean() === 1) {
        <span class="clean-chip" matTooltip="Clean mode: auto-delete after despawn">
          <mat-icon class="info-icon">auto_delete</mat-icon>
          Clean
        </span>
      }
      @if (template()) {
        <span class="template-chip" [matTooltip]="'Template: ' + template()">
          <mat-icon class="info-icon">description</mat-icon>
          {{ template() }}
        </span>
      }
      @if (ping()) {
        <span class="ping-chip" [matTooltip]="'Ping: ' + ping()">
          <mat-icon class="info-icon">notifications</mat-icon>
          {{ ping() }}
        </span>
      }
    </div>
  `,
  styles: [
    `
      .alarm-info-row {
        display: flex;
        flex-wrap: wrap;
        gap: 6px;
        margin-top: 8px;
      }
      .alarm-info-row > span {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        padding: 2px 10px;
        border-radius: 16px;
        font-size: 12px;
        font-weight: 500;
      }
      .info-icon {
        font-size: 14px;
        width: 14px;
        height: 14px;
      }
      .distance-chip {
        background: #e3f2fd;
        color: #1565c0;
      }
      .distance-chip.areas-mode {
        background: #e8f5e9;
        color: #2e7d32;
      }
      .clean-chip {
        background: #fff3e0;
        color: #e65100;
      }
      .template-chip {
        background: #f3e5f5;
        color: #6a1b9a;
      }
      .ping-chip {
        background: #fce4ec;
        color: #c62828;
      }
    `,
  ],
})
export class AlarmInfoComponent {
  distance = input(0);
  clean = input(0);
  template = input<string | null>(null);
  ping = input<string | null>(null);
}
