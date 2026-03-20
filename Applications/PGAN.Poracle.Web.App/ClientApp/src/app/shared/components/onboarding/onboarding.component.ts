import { Component, inject, OnInit, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatStepperModule } from '@angular/material/stepper';
import { RouterLink } from '@angular/router';
import { firstValueFrom, forkJoin } from 'rxjs';
import { LocationDialogComponent } from '../location-dialog/location-dialog.component';
import { AreaService } from '../../../core/services/area.service';
import { DashboardService } from '../../../core/services/dashboard.service';
import { LocationService } from '../../../core/services/location.service';

@Component({
  selector: 'app-onboarding',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatStepperModule, RouterLink],
  template: `
    <div class="onboarding-overlay">
      <div class="onboarding-card" role="dialog" aria-label="Welcome onboarding">
        <div class="onboarding-header">
          <h2>Welcome to PoGO Alerts!</h2>
          <p>Let's get you set up in a few quick steps</p>
        </div>

        <div class="steps">
          @for (step of steps; track step.id; let i = $index) {
            <div
              class="step"
              [class.active]="currentStep() === i"
              [class.completed]="i < currentStep()"
            >
              <div class="step-indicator">
                @if (i < currentStep()) {
                  <mat-icon>check_circle</mat-icon>
                } @else {
                  <span class="step-number">{{ i + 1 }}</span>
                }
              </div>
              <div class="step-content">
                <h3>{{ step.title }}</h3>
                <p>{{ step.description }}</p>
                @if (currentStep() === i) {
                  <div class="step-action">
                    @if (step.id === 'location') {
                      <button mat-flat-button color="primary" (click)="openLocationDialog()">
                        <mat-icon>my_location</mat-icon>
                        {{ locationSet() ? 'Update Location' : 'Set Location' }}
                      </button>
                      @if (locationSet()) {
                        <span class="step-success">
                          <mat-icon>check_circle</mat-icon> Location saved
                        </span>
                      }
                    } @else if (step.route) {
                      <a
                        mat-flat-button
                        color="primary"
                        [routerLink]="step.route"
                        (click)="nextStep()"
                      >
                        {{ step.actionText }}
                      </a>
                    }
                    <button mat-button (click)="nextStep()">
                      {{ i < steps.length - 1 ? (step.id === 'location' && locationSet() ? 'Next' : 'Skip') : 'Get Started!' }}
                    </button>
                  </div>
                }
              </div>
            </div>
          }
        </div>

        <div class="onboarding-footer">
          <button mat-button (click)="dismiss()">Skip Setup</button>
          <div class="step-dots">
            @for (step of steps; track step.id; let i = $index) {
              <div
                class="dot"
                [class.active]="currentStep() === i"
                [class.completed]="i < currentStep()"
              ></div>
            }
          </div>
        </div>
      </div>
    </div>
  `,
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
        opacity: 0.7;
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
      .step-success {
        display: inline-flex;
        align-items: center;
        gap: 4px;
        font-size: 13px;
        font-weight: 500;
        color: #2e7d32;
      }
      .step-success mat-icon {
        font-size: 18px;
        width: 18px;
        height: 18px;
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
})
export class OnboardingComponent implements OnInit {
  private readonly areaService = inject(AreaService);
  private readonly dashboardService = inject(DashboardService);
  private readonly dialog = inject(MatDialog);
  private readonly locationService = inject(LocationService);

  completed = output<void>();
  currentStep = signal(0);
  locationSet = signal(false);
  areasSet = signal(false);
  alarmsExist = signal(false);

  ngOnInit() {
    forkJoin({
      location: this.locationService.getLocation(),
      areas: this.areaService.getSelected(),
      counts: this.dashboardService.getCounts(),
    }).subscribe({
      next: ({ location, areas, counts }) => {
        const hasLocation = location && (location.latitude !== 0 || location.longitude !== 0);
        const hasAreas = areas && areas.length > 0;
        const hasAlarms = counts && Object.values(counts).some(c => (c as number) > 0);

        if (hasLocation) this.locationSet.set(true);
        if (hasAreas) this.areasSet.set(true);
        if (hasAlarms) this.alarmsExist.set(true);

        // If everything is already done, skip the wizard entirely
        if (hasLocation && hasAreas && hasAlarms) {
          this.dismiss();
          return;
        }

        // Auto-advance to first incomplete step
        if (hasLocation && hasAreas) {
          this.currentStep.set(2);
        } else if (hasLocation) {
          this.currentStep.set(1);
        }
      },
      error: () => {},
    });
  }

  steps = [
    {
      id: 'location',
      title: 'Set Your Location',
      description:
        'Your location is used to calculate distances for nearby notifications. Click the button below to search by address, enter coordinates, or use your device GPS.',
      route: null,
      actionText: 'Set Location',
    },
    {
      id: 'areas',
      title: 'Choose Your Areas',
      description: 'Select the areas you want to receive alerts from',
      route: '/areas',
      actionText: 'Choose Areas',
    },
    {
      id: 'alarm',
      title: 'Add Your First Alarm',
      description:
        'Set up a Pokemon, Raid, or Quest alarm to start getting notified',
      route: '/pokemon',
      actionText: 'Add Alarm',
    },
  ];

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

  nextStep() {
    if (this.currentStep() < this.steps.length - 1) {
      this.currentStep.update(s => s + 1);
    } else {
      this.dismiss();
    }
  }

  dismiss() {
    localStorage.setItem('poracle-onboarding-complete', 'true');
    this.completed.emit();
  }
}
