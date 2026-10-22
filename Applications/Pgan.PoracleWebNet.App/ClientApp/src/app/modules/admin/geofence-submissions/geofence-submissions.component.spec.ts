import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';

import { GeofenceSubmissionsComponent } from './geofence-submissions.component';
import { UserGeofence } from '../../../core/models';
import { AdminGeofenceService } from '../../../core/services/admin-geofence.service';
import { AreaService } from '../../../core/services/area.service';
import { ConfigService } from '../../../core/services/config.service';
import { UserGeofenceService } from '../../../core/services/user-geofence.service';
import { ConfirmDialogComponent } from '../../../shared/components/confirm-dialog/confirm-dialog.component';
import { GeofenceDeleteDialogComponent } from '../../../shared/components/geofence-delete-dialog/geofence-delete-dialog.component';

// Mock IntersectionObserver for jsdom
global.IntersectionObserver = jest.fn().mockImplementation(() => ({
  disconnect: jest.fn(),
  observe: jest.fn(),
  unobserve: jest.fn(),
}));

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
    getBounds: jest.fn(),
  })),
  tileLayer: jest.fn(() => ({
    addTo: jest.fn(),
  })),
}));

describe('GeofenceSubmissionsComponent', () => {
  let component: GeofenceSubmissionsComponent;
  let adminGeofenceService: { [K in keyof AdminGeofenceService]?: jest.Mock };
  let areaService: { getGeofencePolygons: jest.Mock };
  let userGeofenceService: { getRegions: jest.Mock };
  let mockDialog: { open: jest.Mock };
  let mockSnackBar: { open: jest.Mock };

  const mockGeofences: UserGeofence[] = [
    {
      id: 1,
      createdAt: '2026-03-20T00:00:00Z',
      displayName: 'Downtown Park',
      groupName: 'west end',
      humanId: '111',
      kojiName: 'pweb_111_downtown_park',
      ownerAvatarUrl: 'https://cdn.discordapp.com/avatars/111/abc.png',
      ownerName: 'UserOne',
      parentId: 10,
      pointCount: 5,
      polygon: [
        [40.0, -74.0],
        [40.1, -74.0],
        [40.1, -74.1],
        [40.0, -74.1],
        [40.0, -74.0],
      ],
      status: 'pending_review',
      submittedAt: '2026-03-21T00:00:00Z',
      updatedAt: '2026-03-21T00:00:00Z',
    },
    {
      id: 2,
      createdAt: '2026-03-19T00:00:00Z',
      displayName: 'Riverfront Trail',
      groupName: 'west end',
      humanId: '222',
      kojiName: 'pweb_222_riverfront_trail',
      ownerName: 'UserTwo',
      parentId: 10,
      pointCount: 8,
      polygon: [],
      status: 'active',
      updatedAt: '2026-03-19T00:00:00Z',
    },
    {
      id: 3,
      createdAt: '2026-03-18T00:00:00Z',
      displayName: 'East Mall',
      groupName: 'east side',
      humanId: '333',
      kojiName: 'pweb_333_east_mall',
      ownerAvatarUrl: 'https://cdn.discordapp.com/avatars/333/ghi.png',
      ownerName: 'UserThree',
      parentId: 20,
      pointCount: 12,
      polygon: [],
      reviewedBy: '999',
      reviewedByAvatarUrl: 'https://cdn.discordapp.com/avatars/999/reviewer.png',
      reviewedByName: 'AdminReviewer',
      reviewNotes: 'Looks good',
      status: 'approved',
      updatedAt: '2026-03-20T00:00:00Z',
    },
    {
      id: 4,
      createdAt: '2026-03-17T00:00:00Z',
      displayName: 'South Bridge',
      groupName: 'east side',
      humanId: '444',
      kojiName: 'pweb_444_south_bridge',
      ownerName: 'UserFour',
      parentId: 20,
      pointCount: 6,
      polygon: [],
      reviewedBy: '999',
      reviewedByName: 'AdminReviewer',
      reviewNotes: 'Overlaps existing zone',
      status: 'rejected',
      updatedAt: '2026-03-18T00:00:00Z',
    },
  ];

  function setup(geofences: UserGeofence[] = mockGeofences) {
    adminGeofenceService = {
      adminDelete: jest.fn().mockReturnValue(of(null)),
      approveSubmission: jest.fn().mockReturnValue(of(mockGeofences[0])),
      getAll: jest.fn().mockReturnValue(of(geofences)),
      getDeleteImpact: jest.fn().mockReturnValue(of({ widened: {}, total: 0 })),
      getSubmissions: jest.fn().mockReturnValue(of(geofences.filter(g => g.status === 'pending_review'))),
      rejectSubmission: jest.fn().mockReturnValue(of(mockGeofences[0])),
    };

    areaService = {
      getGeofencePolygons: jest.fn().mockReturnValue(of([])),
    };

    userGeofenceService = {
      getRegions: jest.fn().mockReturnValue(of([])),
    };

    mockDialog = {
      open: jest.fn().mockReturnValue({ afterClosed: () => of(null) }),
    };

    mockSnackBar = {
      open: jest.fn(),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideTranslateService(),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AdminGeofenceService, useValue: adminGeofenceService },
        { provide: AreaService, useValue: areaService },
        { provide: UserGeofenceService, useValue: userGeofenceService },
        { provide: ConfigService, useValue: { apiHost: 'http://test-api' } },
      ],
      imports: [NoopAnimationsModule],
    })
      .overrideComponent(GeofenceSubmissionsComponent, {
        set: {
          providers: [
            { provide: MatDialog, useValue: mockDialog },
            { provide: MatSnackBar, useValue: mockSnackBar },
          ],
        },
      })
      .compileComponents();

    const fixture = TestBed.createComponent(GeofenceSubmissionsComponent);
    component = fixture.componentInstance;
    return fixture;
  }

  // ─── Basic Setup ─────────────────────────────────────────────────────────────

  it('should create', () => {
    setup();
    expect(component).toBeTruthy();
  });

  it('should load all geofences on init', () => {
    setup();
    component.ngOnInit();

    expect(adminGeofenceService.getAll).toHaveBeenCalled();
    expect(component.allGeofences()).toEqual(mockGeofences);
    expect(component.loading()).toBe(false);
  });

  it('should set loading to false on load error', () => {
    setup();
    adminGeofenceService.getAll!.mockReturnValue(throwError(() => new Error('fail')));

    component.ngOnInit();

    expect(component.loading()).toBe(false);
    expect(mockSnackBar.open).toHaveBeenCalledWith('ADMIN.SNACK_FAILED_LOAD_GEOFENCES', 'TOAST.OK', { duration: 3000 });
  });

  // ─── Status Filtering ───────────────────────────────────────────────────────

  it('lists every fence but the pending ones in the table when the filter is "all"', () => {
    setup();
    component.ngOnInit();
    component.activeFilter.set('all');

    const filtered = component.filteredGeofences();
    expect(filtered).toHaveLength(3);
    expect(filtered.some(g => g.status === 'pending_review')).toBe(false);
  });

  it('puts pending submissions in the review queue', () => {
    setup();
    component.ngOnInit();

    const queue = component.pendingGeofences();
    expect(queue).toHaveLength(1);
    expect(queue[0].status).toBe('pending_review');
  });

  it('orders the queue oldest submission first', () => {
    const older: UserGeofence = { ...mockGeofences[0], id: 9, displayName: 'Older', submittedAt: '2026-03-01T00:00:00Z' };
    setup([mockGeofences[0], older]);
    component.ngOnInit();

    expect(component.pendingGeofences().map(g => g.id)).toEqual([9, 1]);
  });

  it('should filter by active status', () => {
    setup();
    component.ngOnInit();
    component.activeFilter.set('active');

    const filtered = component.filteredGeofences();
    expect(filtered).toHaveLength(1);
    expect(filtered[0].status).toBe('active');
  });

  it('should filter by approved status', () => {
    setup();
    component.ngOnInit();
    component.activeFilter.set('approved');

    const filtered = component.filteredGeofences();
    expect(filtered).toHaveLength(1);
    expect(filtered[0].status).toBe('approved');
  });

  it('should filter by rejected status', () => {
    setup();
    component.ngOnInit();
    component.activeFilter.set('rejected');

    const filtered = component.filteredGeofences();
    expect(filtered).toHaveLength(1);
    expect(filtered[0].status).toBe('rejected');
  });

  // ─── Status Counts ──────────────────────────────────────────────────────────

  it('should compute correct status counts', () => {
    setup();
    component.ngOnInit();

    const counts = component.statusCounts();
    expect(counts.all).toBe(4);
    expect(counts.others).toBe(3);
    expect(counts.pending_review).toBe(1);
    expect(counts.active).toBe(1);
    expect(counts.approved).toBe(1);
    expect(counts.rejected).toBe(1);
  });

  it('should compute zero counts for empty list', () => {
    setup([]);
    component.ngOnInit();

    const counts = component.statusCounts();
    expect(counts.all).toBe(0);
    expect(counts.pending_review).toBe(0);
    expect(counts.active).toBe(0);
    expect(counts.approved).toBe(0);
    expect(counts.rejected).toBe(0);
  });

  // ─── Region Grouping ────────────────────────────────────────────────────────

  it('should contain geofences from multiple groups', () => {
    setup();
    component.ngOnInit();

    const all = component.allGeofences();
    const groups = new Set(all.map(g => g.groupName));
    expect(groups.size).toBe(2);
    expect(groups.has('west end')).toBe(true);
    expect(groups.has('east side')).toBe(true);
  });

  it('should group geofences correctly by groupName', () => {
    setup();
    component.ngOnInit();

    const all = component.allGeofences();
    const grouped = new Map<string, UserGeofence[]>();
    for (const g of all) {
      const list = grouped.get(g.groupName) ?? [];
      list.push(g);
      grouped.set(g.groupName, list);
    }

    expect(grouped.get('west end')).toHaveLength(2);
    expect(grouped.get('east side')).toHaveLength(2);
    expect(grouped.get('west end')![0].displayName).toBe('Downtown Park');
    expect(grouped.get('west end')![1].displayName).toBe('Riverfront Trail');
    expect(grouped.get('east side')![0].displayName).toBe('East Mall');
    expect(grouped.get('east side')![1].displayName).toBe('South Bridge');
  });

  it('should handle geofences with the same groupName from different users', () => {
    setup();
    component.ngOnInit();

    const westEnd = component.allGeofences().filter(g => g.groupName === 'west end');
    const ownerIds = new Set(westEnd.map(g => g.humanId));
    expect(ownerIds.size).toBe(2);
    expect(ownerIds.has('111')).toBe(true);
    expect(ownerIds.has('222')).toBe(true);
  });

  it('should preserve groupName when filtering by status', () => {
    setup();
    component.ngOnInit();
    component.activeFilter.set('approved');

    const filtered = component.filteredGeofences();
    expect(filtered).toHaveLength(1);
    expect(filtered[0].groupName).toBe('east side');
  });

  // ─── Owner Name Resolution ──────────────────────────────────────────────────

  it('should display ownerName when available', () => {
    setup();
    component.ngOnInit();

    const geofence = component.allGeofences().find(g => g.id === 1);
    expect(geofence!.ownerName).toBe('UserOne');
  });

  it('should have ownerAvatarUrl when available', () => {
    setup();
    component.ngOnInit();

    const geofence = component.allGeofences().find(g => g.id === 1);
    expect(geofence!.ownerAvatarUrl).toBe('https://cdn.discordapp.com/avatars/111/abc.png');
  });

  it('should have undefined ownerAvatarUrl when not provided', () => {
    setup();
    component.ngOnInit();

    const geofence = component.allGeofences().find(g => g.id === 2);
    expect(geofence!.ownerAvatarUrl).toBeUndefined();
  });

  // ─── Reviewer Name Resolution ───────────────────────────────────────────────

  it('should display reviewedByName when available', () => {
    setup();
    component.ngOnInit();

    const geofence = component.allGeofences().find(g => g.id === 3);
    expect(geofence!.reviewedByName).toBe('AdminReviewer');
  });

  it('should display reviewedByAvatarUrl when available', () => {
    setup();
    component.ngOnInit();

    const geofence = component.allGeofences().find(g => g.id === 3);
    expect(geofence!.reviewedByAvatarUrl).toBe('https://cdn.discordapp.com/avatars/999/reviewer.png');
  });

  it('should have undefined reviewedByName for unreviewed geofences', () => {
    setup();
    component.ngOnInit();

    const geofence = component.allGeofences().find(g => g.id === 1);
    expect(geofence!.reviewedByName).toBeUndefined();
    expect(geofence!.reviewedByAvatarUrl).toBeUndefined();
  });

  it('should have reviewedBy ID even without reviewedByName', () => {
    const geofencesWithIdOnly: UserGeofence[] = [
      {
        id: 10,
        createdAt: '2026-03-20T00:00:00Z',
        displayName: 'Legacy Fence',
        groupName: 'north',
        humanId: '555',
        kojiName: 'pweb_555_legacy',
        parentId: 30,
        polygon: [],
        reviewedBy: '888',
        status: 'rejected',
        updatedAt: '2026-03-20T00:00:00Z',
      },
    ];

    setup(geofencesWithIdOnly);
    component.ngOnInit();

    const geofence = component.allGeofences()[0];
    expect(geofence.reviewedBy).toBe('888');
    expect(geofence.reviewedByName).toBeUndefined();
    expect(geofence.reviewedByAvatarUrl).toBeUndefined();
  });

  // ─── Point Count ─────────────────────────────────────────────────────────────

  it('should return pointCount from geofence when available', () => {
    setup();
    component.ngOnInit();

    const geofence = component.allGeofences().find(g => g.id === 1)!;
    expect(component.getPointCount(geofence)).toBe(5);
  });

  it('should fall back to polygon length when pointCount is not set', () => {
    const geofence: UserGeofence = {
      id: 99,
      createdAt: '2026-03-20T00:00:00Z',
      displayName: 'No Count',
      groupName: 'test',
      humanId: '999',
      kojiName: 'pweb_999_no_count',
      parentId: 1,
      polygon: [
        [40, -74],
        [41, -74],
        [41, -73],
      ],
      status: 'active',
      updatedAt: '2026-03-20T00:00:00Z',
    };

    setup([geofence]);
    expect(component.getPointCount(geofence)).toBe(3);
  });

  it('should return 0 when neither pointCount nor polygon is available', () => {
    const geofence: UserGeofence = {
      id: 99,
      createdAt: '2026-03-20T00:00:00Z',
      displayName: 'Empty',
      groupName: 'test',
      humanId: '999',
      kojiName: 'pweb_999_empty',
      parentId: 1,
      status: 'active',
      updatedAt: '2026-03-20T00:00:00Z',
    };

    setup([geofence]);
    expect(component.getPointCount(geofence)).toBe(0);
  });

  // ─── Dialog Interactions ─────────────────────────────────────────────────────

  it('should open detail dialog with geofence data', () => {
    setup();
    component.ngOnInit();

    const geofence = component.allGeofences()[0];
    component.openDetailDialog(geofence);

    expect(mockDialog.open).toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({
        data: expect.objectContaining({ geofence }),
      }),
    );
  });

  it('should open review dialog for pending submissions', () => {
    setup();
    component.ngOnInit();

    const pendingGeofence = component.allGeofences().find(g => g.status === 'pending_review')!;
    component.openReviewDialog(pendingGeofence);

    expect(mockDialog.open).toHaveBeenCalledWith(
      expect.anything(),
      expect.objectContaining({
        data: expect.objectContaining({ geofence: pendingGeofence, mode: 'approve' }),
      }),
    );
  });

  it('opens the review dialog on the side of the button pressed', () => {
    const fixture = setup();
    component.ngOnInit();
    fixture.detectChanges();

    const row = (fixture.nativeElement as HTMLElement).querySelector('[data-test="triage-row"]')!;
    (row.querySelector('[data-test="reject"]') as HTMLButtonElement).click();
    expect(mockDialog.open.mock.calls[0][1].data.mode).toBe('reject');

    (row.querySelector('[data-test="approve"]') as HTMLButtonElement).click();
    expect(mockDialog.open.mock.calls[1][1].data.mode).toBe('approve');
  });

  // ─── Admin Delete ────────────────────────────────────────────────────────────

  it('should open confirm dialog before admin delete', async () => {
    setup();
    component.ngOnInit();

    const geofence = component.allGeofences()[0];
    await component.adminDelete(geofence);

    expect(mockDialog.open).toHaveBeenCalled();
  });

  it('should delete geofence and update list when confirmed', async () => {
    setup();
    component.ngOnInit();
    mockDialog.open.mockReturnValue({ afterClosed: () => of(true) });

    const geofence = component.allGeofences()[0];
    await component.adminDelete(geofence);

    expect(adminGeofenceService.adminDelete).toHaveBeenCalledWith(geofence.id, 'keep');
    expect(mockDialog.open.mock.calls[0][0]).toBe(ConfirmDialogComponent);
  });

  it('should ask about widened alarms and delete them by default', async () => {
    setup();
    component.ngOnInit();
    adminGeofenceService.getDeleteImpact!.mockReturnValue(of({ widened: { quest: 2 }, total: 2 }));
    mockDialog.open.mockReturnValue({ afterClosed: () => of('delete') });

    const geofence = component.allGeofences()[0];
    await component.adminDelete(geofence);

    expect(adminGeofenceService.getDeleteImpact).toHaveBeenCalledWith(geofence.id);
    expect(mockDialog.open).toHaveBeenCalledTimes(1);
    expect(mockDialog.open.mock.calls[0][0]).toBe(GeofenceDeleteDialogComponent);
    expect(mockDialog.open.mock.calls[0][1].data).toEqual({ name: geofence.displayName, count: 2, owner: geofence.ownerName });
    expect(adminGeofenceService.adminDelete).toHaveBeenCalledWith(geofence.id, 'delete');
  });

  it('should send keep when the admin keeps the alarms', async () => {
    setup();
    component.ngOnInit();
    adminGeofenceService.getDeleteImpact!.mockReturnValue(of({ widened: { quest: 2 }, total: 2 }));
    mockDialog.open.mockReturnValue({ afterClosed: () => of('keep') });

    const geofence = component.allGeofences()[0];
    await component.adminDelete(geofence);

    expect(adminGeofenceService.adminDelete).toHaveBeenCalledWith(geofence.id, 'keep');
  });

  it('should not delete when the widened-alarms choice is cancelled', async () => {
    setup();
    component.ngOnInit();
    adminGeofenceService.getDeleteImpact!.mockReturnValue(of({ widened: { quest: 2 }, total: 2 }));
    mockDialog.open.mockReturnValue({ afterClosed: () => of(null) });

    await component.adminDelete(component.allGeofences()[0]);

    expect(adminGeofenceService.adminDelete).not.toHaveBeenCalled();
  });

  it('should not delete when the impact cannot be read', async () => {
    setup();
    component.ngOnInit();
    adminGeofenceService.getDeleteImpact!.mockReturnValue(throwError(() => new Error('503')));

    await component.adminDelete(component.allGeofences()[0]);

    expect(mockDialog.open).not.toHaveBeenCalled();
    expect(adminGeofenceService.adminDelete).not.toHaveBeenCalled();
    expect(mockSnackBar.open).toHaveBeenCalledWith('ADMIN.SNACK_FAILED_DELETE_GEOFENCE', 'TOAST.OK', { duration: 4000 });
  });

  // Koji refusing an approve, reject or delete answers 502 with a reason that names the likely cause (the
  // region may no longer exist); a generic "Failed to approve" sent the admin nowhere.
  const kojiRefusal = {
    error: { error: 'The geofence server rejected the request. It may be unavailable, or the region may no longer exist.' },
    status: 502,
  };

  it('shows the Koji reason when a delete is refused with 502', async () => {
    setup();
    component.ngOnInit();
    mockDialog.open.mockReturnValue({ afterClosed: () => of(true) });
    adminGeofenceService.adminDelete!.mockReturnValue(throwError(() => kojiRefusal));

    await component.adminDelete(component.allGeofences()[0]);

    expect(mockSnackBar.open).toHaveBeenCalledWith(kojiRefusal.error.error, 'TOAST.OK', { duration: 4000 });
  });

  it('keeps the translated delete message for a 503 PoracleNG outage', async () => {
    setup();
    component.ngOnInit();
    adminGeofenceService.getDeleteImpact!.mockReturnValue(
      throwError(() => ({ error: { error: 'PoracleNG could not be reached. Try again shortly.' }, status: 503 })),
    );

    await component.adminDelete(component.allGeofences()[0]);

    expect(mockSnackBar.open).toHaveBeenCalledWith('ADMIN.SNACK_FAILED_DELETE_GEOFENCE', 'TOAST.OK', { duration: 4000 });
  });

  it.each([
    ['approve', 'approveSubmission', { action: 'approve', groupName: 'g', parentId: 1, promotedName: 'p' }],
    ['reject', 'rejectSubmission', { action: 'reject', reviewNotes: 'no' }],
  ] as const)('shows the Koji reason when %s is refused with 502', (mode, method, result) => {
    setup();
    component.ngOnInit();
    mockDialog.open.mockReturnValue({ afterClosed: () => of(result) });
    (adminGeofenceService[method] as jest.Mock).mockReturnValue(throwError(() => kojiRefusal));
    const pending = component.allGeofences().find(g => g.status === 'pending_review')!;

    component.openReviewDialog(pending, mode);

    expect(mockSnackBar.open).toHaveBeenCalledWith(kojiRefusal.error.error, 'TOAST.OK', { duration: 4000 });
  });

  it('should not delete when dialog is cancelled', async () => {
    setup();
    component.ngOnInit();
    mockDialog.open.mockReturnValue({ afterClosed: () => of(false) });

    const geofence = component.allGeofences()[0];
    await component.adminDelete(geofence);

    expect(adminGeofenceService.adminDelete).not.toHaveBeenCalled();
  });

  it('should remove geofence from list after successful delete', async () => {
    setup();
    component.ngOnInit();
    mockDialog.open.mockReturnValue({ afterClosed: () => of(true) });

    expect(component.allGeofences()).toHaveLength(4);

    const geofence = component.allGeofences()[0];
    await component.adminDelete(geofence);

    expect(component.allGeofences()).toHaveLength(3);
    expect(component.allGeofences().find(g => g.id === geofence.id)).toBeUndefined();
  });

  it("shows the server's reason when it refuses the delete", async () => {
    setup();
    component.ngOnInit();
    mockDialog.open.mockReturnValue({ afterClosed: () => of(true) });
    adminGeofenceService.adminDelete!.mockReturnValue(throwError(() => ({ error: { error: 'Owner not found.' }, status: 409 })));

    await component.adminDelete(component.allGeofences()[0]);

    expect(mockSnackBar.open).toHaveBeenCalledWith('Owner not found.', 'TOAST.OK', { duration: 4000 });
  });

  it('should show snackbar on delete error', async () => {
    setup();
    component.ngOnInit();
    mockDialog.open.mockReturnValue({ afterClosed: () => of(true) });
    adminGeofenceService.adminDelete!.mockReturnValue(throwError(() => new Error('fail')));

    const geofence = component.allGeofences()[0];
    await component.adminDelete(geofence);

    expect(mockSnackBar.open).toHaveBeenCalledWith('ADMIN.SNACK_FAILED_DELETE_GEOFENCE', 'TOAST.OK', { duration: 4000 });
  });

  // ─── Geofence Reference Polygons ────────────────────────────────────────────

  it('should fetch reference geofences on init', () => {
    setup();
    component.ngOnInit();

    expect(areaService.getGeofencePolygons).toHaveBeenCalled();
  });

  // ─── Template Rendering (DOM) ────────────────────────────────────────────────

  it('renders the table filter without a Pending option, since pending fences are in the queue', () => {
    const fixture = setup();
    component.ngOnInit();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const filterToggles = Array.from(compiled.querySelectorAll('.filter-tabs mat-button-toggle'));
    expect(filterToggles.length).toBe(4); // all, active, approved, rejected
    expect(filterToggles.some(t => t.textContent?.includes('ADMIN.GEO_STATUS_PENDING'))).toBe(false);
  });

  it('renders the queue above the table, one wide row per pending submission', () => {
    const fixture = setup();
    component.ngOnInit();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const queue = compiled.querySelector('[data-test="review-queue"]')!;
    const others = compiled.querySelector('[data-test="other-fences"]')!;
    expect(queue.compareDocumentPosition(others) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    const rows = queue.querySelectorAll('[data-test="triage-row"]');
    expect(rows.length).toBe(1);
    expect(rows[0].querySelector('.map-thumbnail[data-geofence-id="1"]')).toBeTruthy();
    expect(rows[0].textContent).toContain('Downtown Park');
    expect(rows[0].querySelector('[data-test="approve"]')).toBeTruthy();
    expect(rows[0].querySelector('[data-test="reject"]')).toBeTruthy();

    expect(others.querySelectorAll('tbody tr').length).toBe(3);
  });

  it('says so when nothing is waiting, and still shows the other fences', () => {
    const fixture = setup(mockGeofences.filter(g => g.status !== 'pending_review'));
    component.ngOnInit();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('[data-test="queue-empty"]')?.textContent).toContain('ADMIN.NO_PENDING_SUBMISSIONS');
    expect(compiled.querySelectorAll('[data-test="other-fences"] tbody tr').length).toBe(3);
  });

  it('names the owner in the queue row', () => {
    const fixture = setup();
    component.ngOnInit();
    fixture.detectChanges();

    const owner = (fixture.nativeElement as HTMLElement).querySelector('.triage-owner-name');
    expect(owner).toBeTruthy();
  });

  it('falls back to humanId in the table when ownerName is not available', () => {
    const noNameGeofences: UserGeofence[] = [
      {
        id: 50,
        createdAt: '2026-03-20T00:00:00Z',
        displayName: 'No Name Fence',
        groupName: 'test',
        humanId: '777',
        kojiName: 'pweb_777_no_name',
        parentId: 1,
        polygon: [],
        status: 'active',
        updatedAt: '2026-03-20T00:00:00Z',
      },
    ];

    const fixture = setup(noNameGeofences);
    component.ngOnInit();
    fixture.detectChanges();

    const ownerSpan = (fixture.nativeElement as HTMLElement).querySelector('.list-owner-name');
    expect(ownerSpan?.textContent).toContain('777');
  });

  it('shows the region, or "No region" when there is none', () => {
    const fixture = setup([...mockGeofences, { ...mockGeofences[1], id: 60, groupName: '' }]);
    component.ngOnInit();
    fixture.detectChanges();

    const regions = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('td.col-region')).map(td => td.textContent?.trim());
    expect(regions).toContain('west end');
    expect(regions).toContain('ADMIN.NO_REGION');
  });

  it('shows the reviewer and review notes in the table', () => {
    const fixture = setup();
    component.ngOnInit();
    component.activeFilter.set('rejected');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.list-reviewer-name')?.textContent).toContain('AdminReviewer');
    expect(compiled.querySelector('.list-notes')?.textContent).toContain('Overlaps existing zone');
  });

  it('offers Review for pending and rejected submissions, matching what the API accepts', () => {
    // ApprovableStatuses is { pending_review, rejected } -- an admin reconsidering a rejection is the
    // intended case, and gating on pending_review alone made a mistaken rejection irreversible from the
    // SPA. See #649. Pending ones are decided in the queue; a rejected one keeps Review in the table.
    const fixture = setup();
    component.ngOnInit();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelectorAll('[data-test="triage-row"] [data-test="approve"]')).toHaveLength(1);
    expect(compiled.querySelectorAll('[data-test="other-fences"] [data-test="review"]')).toHaveLength(1);
    expect(component.canReview({ status: 'pending_review' })).toBe(true);
    expect(component.canReview({ status: 'rejected' })).toBe(true);
    expect(component.canReview({ status: 'approved' })).toBe(false);
    expect(component.canReview({ status: 'active' })).toBe(false);
  });

  it('should show empty state when there are no geofences at all', () => {
    const fixture = setup([]);
    component.ngOnInit();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.empty-state')).toBeTruthy();
  });

  it('should show skeleton rows while loading', () => {
    // Use NEVER to prevent getAll from completing, keeping loading=true
    const { NEVER } = require('rxjs');
    const fixture = setup();
    adminGeofenceService.getAll!.mockReturnValue(NEVER);
    component.ngOnInit();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelectorAll('.skeleton-row').length).toBe(2);
  });

  it('should display promoted name for approved geofences', () => {
    const approvedWithName: UserGeofence[] = [
      {
        id: 100,
        createdAt: '2026-03-20T00:00:00Z',
        displayName: 'Promoted Fence',
        groupName: 'test',
        humanId: '123',
        kojiName: 'pweb_123_promoted',
        parentId: 1,
        polygon: [],
        promotedName: 'Official Park Area',
        status: 'approved',
        updatedAt: '2026-03-20T00:00:00Z',
      },
    ];

    const fixture = setup(approvedWithName);
    component.ngOnInit();
    fixture.detectChanges();

    const sub = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.list-sub')).find(el =>
      el.textContent?.includes('ADMIN.PROMOTED_AS'),
    );
    expect(sub?.textContent).toContain('Official Park Area');
  });

  describe('overlap with a public area', () => {
    const square = (lat: number, lng: number, d: number): [number, number][] => [
      [lat, lng],
      [lat + d, lng],
      [lat + d, lng + d],
      [lat, lng + d],
    ];

    it('names the public area the centre of the submission falls inside', () => {
      setup();
      areaService.getGeofencePolygons.mockReturnValue(of([{ id: 7, name: 'Downtown', path: square(39.9, -74.2, 0.5) }]));
      component.ngOnInit();

      expect(component.overlaps().get(1)).toBe('Downtown');
    });

    it('names nothing when the centre is outside every public area, or inside only the fence itself', () => {
      setup();
      areaService.getGeofencePolygons.mockReturnValue(
        of([
          { id: 7, name: 'Elsewhere', path: square(10, 10, 0.5) },
          { id: 8, name: 'pweb_111_downtown_park', path: square(39.9, -74.2, 0.5) },
        ]),
      );
      component.ngOnInit();

      expect(component.overlaps().has(1)).toBe(false);
    });
  });

  describe('approval and the owner areas', () => {
    function approveWith(response: Partial<UserGeofence>): void {
      setup();
      component.ngOnInit();
      const pending = component.allGeofences().find(g => g.status === 'pending_review')!;
      adminGeofenceService['approveSubmission'].mockReturnValue(of({ ...pending, ...response, status: 'approved' }));
      mockDialog.open.mockReturnValue({ afterClosed: () => of({ action: 'approve', promotedName: 'Official' }) });
      component.openReviewDialog(pending);
    }

    it('warns the admin when the owner areas were not moved', () => {
      approveWith({ ownerAreasRenamed: false });

      expect(mockSnackBar.open).toHaveBeenCalledWith('ADMIN.SNACK_APPROVED_AREAS_NOT_MOVED', 'TOAST.OK', { duration: 8000 });
      expect(mockSnackBar.open).not.toHaveBeenCalledWith('ADMIN.SNACK_APPROVED', expect.anything(), expect.anything());
    });

    it.each([true, null, undefined])('shows the plain approval when ownerAreasRenamed is %s', renamed => {
      approveWith({ ownerAreasRenamed: renamed });

      expect(mockSnackBar.open).toHaveBeenCalledWith('ADMIN.SNACK_APPROVED', 'TOAST.OK', { duration: 3000 });
      expect(mockSnackBar.open).not.toHaveBeenCalledWith('ADMIN.SNACK_APPROVED_AREAS_NOT_MOVED', expect.anything(), expect.anything());
    });
  });
});
