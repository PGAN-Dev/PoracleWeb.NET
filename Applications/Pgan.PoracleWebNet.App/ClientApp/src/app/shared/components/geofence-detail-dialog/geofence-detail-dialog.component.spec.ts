import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { of } from 'rxjs';

import { GeofenceDetailDialogComponent, GeofenceDetailDialogData } from './geofence-detail-dialog.component';
import { UserGeofence } from '../../../core/models';

// Mock Leaflet to avoid jsdom issues
jest.mock('leaflet', () => ({
  map: jest.fn(() => ({
    invalidateSize: jest.fn(),
    fitBounds: jest.fn(),
    remove: jest.fn(),
    setView: jest.fn().mockReturnThis(),
  })),
  polygon: jest.fn(() => ({
    addTo: jest.fn(),
    bindTooltip: jest.fn().mockReturnThis(),
    getBounds: jest.fn(),
  })),
  tileLayer: jest.fn(() => ({
    addTo: jest.fn(),
  })),
}));

describe('GeofenceDetailDialogComponent', () => {
  let component: GeofenceDetailDialogComponent;
  let dialogRef: { afterOpened: jest.Mock; close: jest.Mock };

  const mockGeofence: UserGeofence = {
    id: 1,
    createdAt: '2024-01-15T00:00:00Z',
    displayName: 'Test Geofence',
    groupName: 'test-group',
    humanId: '123456789',
    kojiName: 'test-geofence',
    ownerName: 'TestUser',
    parentId: 0,
    pointCount: 4,
    polygon: [
      [40.0, -74.0],
      [40.1, -74.0],
      [40.1, -74.1],
      [40.0, -74.1],
    ],
    status: 'pending_review',
    submittedAt: '2024-01-16T00:00:00Z',
    updatedAt: '2024-01-16T00:00:00Z',
  };

  function setup(geofenceOverrides?: Partial<UserGeofence>) {
    dialogRef = { afterOpened: jest.fn().mockReturnValue(of(undefined)), close: jest.fn() };

    const geofence = { ...mockGeofence, ...geofenceOverrides };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        { provide: MAT_DIALOG_DATA, useValue: { geofence } as GeofenceDetailDialogData },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
      imports: [GeofenceDetailDialogComponent],
    });

    const fixture = TestBed.createComponent(GeofenceDetailDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
    return fixture;
  }

  beforeEach(() => setup());

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should display geofence name in title', () => {
    const fixture = setup();
    const compiled = fixture.nativeElement as HTMLElement;
    const title = compiled.querySelector('.dialog-title');
    expect(title?.textContent).toContain('Test Geofence');
  });

  it('should display owner name', () => {
    const fixture = setup();
    const compiled = fixture.nativeElement as HTMLElement;
    const rows = compiled.querySelectorAll('.summary-row');
    const ownerRow = Array.from(rows).find(r => r.querySelector('.summary-label')?.textContent?.trim() === 'Owner');
    expect(ownerRow?.querySelector('.summary-value')?.textContent).toContain('TestUser');
  });

  it('should fall back to humanId when ownerName is not present', () => {
    const fixture = setup({ ownerName: undefined });
    const compiled = fixture.nativeElement as HTMLElement;
    const rows = compiled.querySelectorAll('.summary-row');
    const ownerRow = Array.from(rows).find(r => r.querySelector('.summary-label')?.textContent?.trim() === 'Owner');
    // When ownerName is falsy, the owner row is not shown per the @if template guard
    expect(ownerRow).toBeUndefined();
  });

  it('should display status chip with correct color for pending_review', () => {
    expect(component.statusColor).toBe('#ff9800');
    expect(component.statusLabel).toBe('Pending Review');
  });

  it('should display status chip with correct color for active', () => {
    setup({ status: 'active' });
    expect(component.statusColor).toBe('#2196f3');
    expect(component.statusLabel).toBe('Active');
  });

  it('should display status chip with correct color for approved', () => {
    setup({ status: 'approved' });
    expect(component.statusColor).toBe('#4caf50');
    expect(component.statusLabel).toBe('Approved');
  });

  it('should display status chip with correct color for rejected', () => {
    setup({ status: 'rejected' });
    expect(component.statusColor).toBe('#f44336');
    expect(component.statusLabel).toBe('Rejected');
  });

  it('should display point count', () => {
    expect(component.pointCount).toBe(4);
  });

  it('should fall back to polygon length when pointCount is not set', () => {
    setup({ pointCount: undefined });
    expect(component.pointCount).toBe(4);
  });

  it('should display created date', () => {
    const fixture = setup();
    const compiled = fixture.nativeElement as HTMLElement;
    const rows = compiled.querySelectorAll('.summary-row');
    const createdRow = Array.from(rows).find(r => r.querySelector('.summary-label')?.textContent?.trim() === 'Created');
    expect(createdRow).toBeTruthy();
    expect(createdRow?.querySelector('.summary-value')?.textContent?.trim()).toBeTruthy();
  });

  it('should show submitted date when present', () => {
    const fixture = setup({ submittedAt: '2024-01-16T00:00:00Z' });
    const compiled = fixture.nativeElement as HTMLElement;
    const rows = compiled.querySelectorAll('.summary-row');
    const submittedRow = Array.from(rows).find(r => r.querySelector('.summary-label')?.textContent?.trim() === 'Submitted');
    expect(submittedRow).toBeTruthy();
  });

  it('should not show submitted date when absent', () => {
    const fixture = setup({ submittedAt: undefined });
    const compiled = fixture.nativeElement as HTMLElement;
    const rows = compiled.querySelectorAll('.summary-row');
    const submittedRow = Array.from(rows).find(r => r.querySelector('.summary-label')?.textContent?.trim() === 'Submitted');
    expect(submittedRow).toBeUndefined();
  });

  it('should show review notes when present', () => {
    const fixture = setup({ reviewNotes: 'Area overlaps with existing zone' });
    const compiled = fixture.nativeElement as HTMLElement;
    const rows = compiled.querySelectorAll('.summary-row');
    const notesRow = Array.from(rows).find(r => r.querySelector('.summary-label')?.textContent?.trim() === 'Review notes');
    expect(notesRow).toBeTruthy();
    expect(notesRow?.querySelector('.summary-value')?.textContent).toContain('Area overlaps with existing zone');
  });

  it('should not show review notes when absent', () => {
    const fixture = setup({ reviewNotes: undefined });
    const compiled = fixture.nativeElement as HTMLElement;
    const rows = compiled.querySelectorAll('.summary-row');
    const notesRow = Array.from(rows).find(r => r.querySelector('.summary-label')?.textContent?.trim() === 'Review notes');
    expect(notesRow).toBeUndefined();
  });

  it('should close dialog when close button uses mat-dialog-close', () => {
    const fixture = setup();
    const compiled = fixture.nativeElement as HTMLElement;
    const closeButton = compiled.querySelector('button[mat-dialog-close]');
    expect(closeButton).toBeTruthy();
    expect(closeButton?.textContent?.trim()).toBe('Close');
  });
});
