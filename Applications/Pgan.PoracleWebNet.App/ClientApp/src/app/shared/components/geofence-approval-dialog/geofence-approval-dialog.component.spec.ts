import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';

import {
  GeofenceApprovalDialogComponent,
  GeofenceApprovalDialogData,
  GeofenceApprovalDialogResult,
} from './geofence-approval-dialog.component';
import { GeofenceRegion, UserGeofence } from '../../../core/models';

describe('GeofenceApprovalDialogComponent', () => {
  let component: GeofenceApprovalDialogComponent;
  let dialogRef: { close: jest.Mock };

  const mockGeofence: UserGeofence = {
    id: 1,
    createdAt: '2026-03-20T00:00:00Z',
    displayName: 'Downtown',
    groupName: 'City Center',
    kojiName: 'pweb_111_downtown',
    parentId: 5,
    status: 'pending_review',
    submittedAt: '2026-03-21T00:00:00Z',
    updatedAt: '2026-03-21T00:00:00Z',
  };

  const regions: GeofenceRegion[] = [
    { id: 5, name: 'city', displayName: 'City Center' },
    { id: 7, name: 'suburbs', displayName: 'Suburbs' },
  ];

  function setup(data?: Partial<GeofenceApprovalDialogData>) {
    dialogRef = { close: jest.fn() };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        { provide: MAT_DIALOG_DATA, useValue: { geofence: mockGeofence, regions: [], ...data } },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
      imports: [GeofenceApprovalDialogComponent],
    });

    const fixture = TestBed.createComponent(GeofenceApprovalDialogComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  }

  beforeEach(() => setup());

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should default to approve mode', () => {
    expect(component.mode).toBe('approve');
  });

  it('should initialize promotedName from geofence displayName', () => {
    expect(component.promotedName).toBe('Downtown');
  });

  it('should switch to reject mode', () => {
    component.mode = 'reject';
    expect(component.mode).toBe('reject');
  });

  it('should close with approve result on approve', () => {
    component.promotedName = 'Downtown Official';
    component.onApprove();

    expect(dialogRef.close).toHaveBeenCalledWith({
      action: 'approve',
      promotedName: 'Downtown Official',
    } as GeofenceApprovalDialogResult);
  });

  it('should close with undefined promotedName when empty', () => {
    component.promotedName = '   ';
    component.onApprove();

    expect(dialogRef.close).toHaveBeenCalledWith({
      action: 'approve',
      promotedName: undefined,
    } as GeofenceApprovalDialogResult);
  });

  it('should close with reject result on reject', () => {
    component.mode = 'reject';
    component.reviewNotes = 'Area too large';
    component.onReject();

    expect(dialogRef.close).toHaveBeenCalledWith({
      action: 'reject',
      reviewNotes: 'Area too large',
    } as GeofenceApprovalDialogResult);
  });

  it('should close with null on cancel', () => {
    component.onCancel();
    expect(dialogRef.close).toHaveBeenCalledWith(null);
  });

  it('should have reject button disabled when reviewNotes is empty', () => {
    component.mode = 'reject';
    component.reviewNotes = '';
    // The template uses [disabled]="!reviewNotes.trim()"
    expect(component.reviewNotes.trim()).toBe('');
  });

  it('should have reject button enabled when reviewNotes has content', () => {
    component.mode = 'reject';
    component.reviewNotes = 'Some reason';
    expect(component.reviewNotes.trim()).toBeTruthy();
  });

  it('should trim whitespace from reviewNotes on reject', () => {
    component.mode = 'reject';
    component.reviewNotes = '  Needs work  ';
    component.onReject();

    expect(dialogRef.close).toHaveBeenCalledWith({
      action: 'reject',
      reviewNotes: 'Needs work',
    } as GeofenceApprovalDialogResult);
  });

  it('should trim promotedName on approve', () => {
    component.promotedName = '  Downtown Official  ';
    component.onApprove();

    expect(dialogRef.close).toHaveBeenCalledWith({
      action: 'approve',
      promotedName: 'Downtown Official',
    } as GeofenceApprovalDialogResult);
  });

  it('should switch between approve and reject modes', () => {
    expect(component.mode).toBe('approve');
    component.mode = 'reject';
    expect(component.mode).toBe('reject');
    component.mode = 'approve';
    expect(component.mode).toBe('approve');
  });

  it('should initialize reviewNotes as empty string', () => {
    expect(component.reviewNotes).toBe('');
  });

  describe('region selection (#314)', () => {
    it('should report hasRegions=false and omit region overrides when no regions exist', () => {
      setup({ regions: [] });
      component.promotedName = 'Downtown Official';
      component.onApprove();

      expect(component.hasRegions).toBe(false);
      expect(dialogRef.close).toHaveBeenCalledWith({
        action: 'approve',
        promotedName: 'Downtown Official',
      } as GeofenceApprovalDialogResult);
    });

    it('should default the selected region to the submission parentId', () => {
      setup({ regions });
      expect(component.hasRegions).toBe(true);
      expect(component.selectedRegionId).toBe(5);
    });

    it('should send the defaulted region on approve when untouched', () => {
      setup({ regions });
      component.promotedName = 'Downtown Official';
      component.onApprove();

      expect(dialogRef.close).toHaveBeenCalledWith({
        action: 'approve',
        groupName: 'City Center',
        parentId: 5,
        promotedName: 'Downtown Official',
      } as GeofenceApprovalDialogResult);
    });

    it('should send the admin-chosen region override on approve', () => {
      setup({ regions });
      component.onRegionPicked({ id: 7, label: 'Suburbs' });
      component.promotedName = 'Downtown';
      component.onApprove();

      expect(dialogRef.close).toHaveBeenCalledWith({
        action: 'approve',
        groupName: 'Suburbs',
        parentId: 7,
        promotedName: 'Downtown',
      } as GeofenceApprovalDialogResult);
    });

    it('should send parentId 0 / empty group when the region is cleared', () => {
      setup({ regions });
      component.onRegionPicked({ label: '' });
      component.promotedName = 'Downtown';
      component.onApprove();

      expect(dialogRef.close).toHaveBeenCalledWith({
        action: 'approve',
        groupName: '',
        parentId: 0,
        promotedName: 'Downtown',
      } as GeofenceApprovalDialogResult);
    });
  });

  // The server rejects names outside ^[a-zA-Z0-9 \-'.()&]+$ or over 50 chars, and used to answer 404
  // for it — so the SPA toasted "Not found" for a submission that was plainly visible. Catching it here
  // means the admin never makes that request. See #421.
  describe('promoted name validation (#421)', () => {
    it.each(['Downtown / Uptown', 'Park_West', 'North Side, East', 'bad<>name;drop'])('rejects %s, which the server would refuse', name => {
      component.promotedName = name;
      expect(component.promotedNameError).toBe('ADMIN.APPROVAL_PROMOTED_NAME_INVALID');
      expect(component.canApprove).toBe(false);
    });

    it.each(['Downtown Official', "O'Fallon Park", 'Midlo - Westchester', 'Parks (North) & South', 'Area 51'])('accepts %s', name => {
      component.promotedName = name;
      expect(component.promotedNameError).toBeNull();
      expect(component.canApprove).toBe(true);
    });

    it('rejects a name over 50 characters', () => {
      component.promotedName = 'a'.repeat(51);
      expect(component.promotedNameError).toBe('ADMIN.APPROVAL_PROMOTED_NAME_TOO_LONG');
    });

    it('accepts exactly 50 characters', () => {
      component.promotedName = 'a'.repeat(50);
      expect(component.promotedNameError).toBeNull();
    });

    it('allows an empty name, which means keep the current display name', () => {
      component.promotedName = '   ';
      expect(component.promotedNameError).toBeNull();
      expect(component.canApprove).toBe(true);
    });

    it('does not block rejecting just because the promoted name is invalid', () => {
      component.promotedName = 'Downtown / Uptown';
      component.mode = 'reject';
      expect(component.canApprove).toBe(true);
    });
  });
});
