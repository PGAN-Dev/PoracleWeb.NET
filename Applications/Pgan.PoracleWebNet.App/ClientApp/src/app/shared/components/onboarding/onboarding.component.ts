import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, output, signal, computed } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatStepperModule } from '@angular/material/stepper';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { firstValueFrom, forkJoin, catchError, of } from 'rxjs';

import { AreaService } from '../../../core/services/area.service';
import { DashboardService } from '../../../core/services/dashboard.service';
import { LocationService } from '../../../core/services/location.service';
import { SettingsService } from '../../../core/services/settings.service';
import { LocationDialogComponent } from '../location-dialog/location-dialog.component';

@Component({
  imports: [CommonModule, MatButtonModule, MatIconModule, MatStepperModule, RouterLink, TranslateModule],
  selector: 'app-onboarding',
  standalone: true,
  styles: [
    `
      .onboarding-overlay {
        position: fixed;
        inset: 0;
        background: rgba(0, 0, 0, 0.6);
        display: flex;
        align-items: center;
        justify-content: center;
        z-index: 3000;
        padding: 16px;
        backdrop-filter: blur(4px);
      }
      .onboarding-card {
        background: var(--card-bg, #fff);
        border-radius: 20px;
        padding: 32px;
        max-width: 520px;
        width: 100%;
        box-shadow: 0 16px 48px rgba(0, 0, 0, 0.2);
      }
      .onboarding-header {
        text-align: center;
        margin-bottom: 32px;
      }
      .onboarding-header h2 {
        font-family: 'Plus Jakarta Sans', sans-serif;
        font-weight: 700;
        font-size: 24px;
        margin: 0 0 8px;
      }
      .onboarding-header p {
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
        margin: 0;
        font-size: 15px;
      }
      .steps {
        display: flex;
        flex-direction: column;
        gap: 4px;
      }
      .step {
        display: flex;
        gap: 16px;
        padding: 16px;
        border-radius: 12px;
        transition: background 0.2s;
        opacity: 0.5;
      }
      .step.active {
        background: rgba(25, 118, 210, 0.04);
        opacity: 1;
      }
      .step.completed {
        opacity: 0.8;
      }
      .step.completed:not(.active) .step-content p {
        color: #2e7d32;
      }
      .step-indicator {
        flex-shrink: 0;
        width: 36px;
        height: 36px;
        display: flex;
        align-items: center;
        justify-content: center;
      }
      .step-number {
        width: 32px;
        height: 32px;
        border-radius: 50%;
        border: 2px solid var(--card-border, rgba(0, 0, 0, 0.12));
        display: flex;
        align-items: center;
        justify-content: center;
        font-weight: 600;
        font-size: 14px;
      }
      .step.active .step-number {
        border-color: #1976d2;
        color: #1976d2;
        background: rgba(25, 118, 210, 0.08);
      }
      .step.completed mat-icon {
        color: #4caf50;
      }
      .step-content h3 {
        margin: 0 0 4px;
        font-size: 15px;
        font-weight: 600;
      }
      .step-content p {
        margin: 0;
        font-size: 13px;
        color: var(--text-secondary, rgba(0, 0, 0, 0.54));
      }
      .step-action {
        margin-top: 12px;
        display: flex;
        gap: 8px;
        align-items: center;
        flex-wrap: wrap;
      }
      .onboarding-footer {
        display: flex;
        justify-content: space-between;
        align-items: center;
        margin-top: 24px;
        padding-top: 16px;
        border-top: 1px solid var(--divider, rgba(0, 0, 0, 0.08));
      }
      .step-dots {
        display: flex;
        gap: 6px;
      }
      .dot {
        width: 8px;
        height: 8px;
        border-radius: 50%;
        background: var(--skeleton-bg, rgba(0, 0, 0, 0.12));
        transition:
          background 0.2s,
          transform 0.2s;
      }
      .dot.active {
        background: #1976d2;
        transform: scale(1.2);
      }
      .dot.completed {
        background: #4caf50;
      }
      @media (max-width: 599px) {
        .onboarding-card {
          padding: 24px 20px;
          border-radius: 16px;
        }
      }
    `,
  ],
  template: `
    <div class="onboarding-overlay">
      <div class="onboarding-card" role="dialog" [attr.aria-label]="'ONBOARDING.ARIA_LABEL' | translate">
        <div class="onboarding-header">
          <h2>{{ (allComplete() ? 'ONBOARDING.TITLE_COMPLETE' : 'ONBOARDING.TITLE_WELCOME') | translate: { title: siteTitle() } }}</h2>
          <p>{{ (allComplete() ? 'ONBOARDING.SUBTITLE_COMPLETE' : 'ONBOARDING.SUBTITLE_WELCOME') | translate }}</p>
        </div>

        <div class="steps">
          @for (step of steps; track step.id; let i = $index) {
            <div class="step" [class.active]="currentStep() === i" [class.completed]="stepComplete(i)">
              <div class="step-indicator">
                @if (stepComplete(i)) {
                  <mat-icon>check_circle</mat-icon>
                } @else {
                  <span class="step-number">{{ i + 1 }}</span>
                }
              </div>
              <div class="step-content">
                <h3>{{ step.titleKey | translate }}</h3>
                <p>{{ (stepComplete(i) ? step.doneKey : step.descKey) | translate }}</p>
                @if (currentStep() === i) {
                  <div class="step-action">
                    @if (step.id === 'location') {
                      <button mat-flat-button color="primary" (click)="openLocationDialog()">
                        <mat-icon>my_location</mat-icon>
                        {{ (locationSet() ? 'ONBOARDING.STEP_LOCATION_UPDATE' : 'ONBOARDING.STEP_LOCATION_SET') | translate }}
                      </button>
                    } @else if (step.id === 'areas') {
                      <a mat-flat-button color="primary" [routerLink]="step.route" (click)="navigateAway()">
                        <mat-icon>map</mat-icon>
                        {{ (areasSet() ? 'ONBOARDING.STEP_AREAS_EDIT' : 'ONBOARDING.STEP_AREAS_CHOOSE') | translate }}
                      </a>
                    } @else if (step.id === 'alarm') {
                      <a mat-flat-button color="primary" [routerLink]="step.route" (click)="navigateAway()">
                        <mat-icon>add_alert</mat-icon>
                        {{ (alarmsExist() ? 'ONBOARDING.STEP_ALARM_MANAGE' : 'ONBOARDING.STEP_ALARM_ADD') | translate }}
                      </a>
                    }
                    <button mat-button (click)="nextStep()">
                      {{
                        (stepComplete(i) ? 'ONBOARDING.NEXT' : i < steps.length - 1 ? 'ONBOARDING.SKIP' : 'ONBOARDING.GET_STARTED')
                          | translate
                      }}
                    </button>
                  </div>
                }
              </div>
            </div>
          }
        </div>

        <div class="onboarding-footer">
          <button mat-button (click)="dismiss()">
            {{ (allComplete() ? 'ONBOARDING.CLOSE' : 'ONBOARDING.SKIP_SETUP') | translate }}
          </button>
          @if (allComplete()) {
            <button mat-flat-button color="primary" (click)="dismiss()">{{ 'ONBOARDING.LETS_GO' | translate }}</button>
          } @else {
            <div class="step-dots">
              @for (step of steps; track step.id; let i = $index) {
                <div class="dot" [class.active]="currentStep() === i" [class.completed]="stepComplete(i)"></div>
              }
            </div>
          }
        </div>
      </div>
    </div>
  `,
})
export class OnboardingComponent implements OnInit {
  private readonly areaService = inject(AreaService);
  private readonly dashboardService = inject(DashboardService);
  private readonly dialog = inject(MatDialog);
  private readonly locationService = inject(LocationService);
  private readonly settingsService = inject(SettingsService);

  alarmsExist = signal(false);
  areasSet = signal(false);
  locationSet = signal(false);
  allComplete = computed(() => this.locationSet() && this.areasSet() && this.alarmsExist());
  completed = output<void>();
  currentStep = signal(0);
  navigatedAway = output<void>();

  siteTitle = computed(() => this.settingsService.siteSettings()['custom_title'] || 'DM Alerts');

  steps = [
    {
      id: 'location',
      descKey: 'ONBOARDING.STEP_LOCATION_DESC',
      doneKey: 'ONBOARDING.STEP_LOCATION_DONE',
      route: null,
      titleKey: 'ONBOARDING.STEP_LOCATION_TITLE',
    },
    {
      id: 'areas',
      descKey: 'ONBOARDING.STEP_AREAS_DESC',
      doneKey: 'ONBOARDING.STEP_AREAS_DONE',
      route: '/areas',
      titleKey: 'ONBOARDING.STEP_AREAS_TITLE',
    },
    {
      id: 'alarm',
      descKey: 'ONBOARDING.STEP_ALARM_DESC',
      doneKey: 'ONBOARDING.STEP_ALARM_DONE',
      route: '/pokemon',
      titleKey: 'ONBOARDING.STEP_ALARM_TITLE',
    },
  ];

  dismiss() {
    localStorage.setItem('poracle-onboarding-complete', 'true');
    this.completed.emit();
  }

  navigateAway() {
    this.navigatedAway.emit();
  }

  nextStep() {
    if (this.currentStep() < this.steps.length - 1) {
      this.currentStep.update(s => s + 1);
    } else {
      this.dismiss();
    }
  }

  ngOnInit() {
    forkJoin({
      areas: this.areaService.getSelected().pipe(catchError(() => of([]))),
      counts: this.dashboardService.getCounts().pipe(catchError(() => of(null))),
      location: this.locationService.getLocation().pipe(catchError(() => of(null))),
    }).subscribe({
      error: () => {},
      next: ({ areas, counts, location }) => {
        const hasLocation = !!(location && (location.latitude !== 0 || location.longitude !== 0));
        const hasAreas = !!(areas && areas.length > 0);
        const hasAlarms = !!(counts && Object.values(counts).some(c => (c as number) > 0));

        if (hasLocation) this.locationSet.set(true);
        if (hasAreas) this.areasSet.set(true);
        if (hasAlarms) this.alarmsExist.set(true);

        // Auto-advance to first incomplete step
        if (!hasLocation) {
          this.currentStep.set(0);
        } else if (!hasAreas) {
          this.currentStep.set(1);
        } else if (!hasAlarms) {
          this.currentStep.set(2);
        } else {
          // All complete — show step 0 so user sees everything checked off
          this.currentStep.set(0);
        }
      },
    });
  }

  async openLocationDialog() {
    const dialogRef = this.dialog.open(LocationDialogComponent, {
      width: '600px',
      data: null,
    });
    const result = await firstValueFrom(dialogRef.afterClosed());
    if (result && (result.latitude !== 0 || result.longitude !== 0)) {
      await firstValueFrom(this.locationService.setLocation(result));
      this.locationSet.set(true);
    }
  }

  stepComplete(index: number): boolean {
    switch (index) {
      case 0:
        return this.locationSet();
      case 1:
        return this.areasSet();
      case 2:
        return this.alarmsExist();
      default:
        return false;
    }
  }
}
