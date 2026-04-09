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
      getSubmissions: jest.fn().mockReturnValue(of(geofences.filter(g => g.status === 'pending_review'))),
      rejectSubmission: jest.fn().mockReturnValue(of(mockGeofences[0])),
    };

    areaService = {
      getGeofencePolygons: jest.fn().mockReturnValue(of([])),
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

  it('should show all geofences when filter is "all"', () => {
    setup();
    component.ngOnInit();
    component.activeFilter.set('all');

    expect(component.filteredGeofences()).toHaveLength(4);
  });

  it('should filter by pending_review status', () => {
    setup();
    component.ngOnInit();
    component.activeFilter.set('pending_review');

    const filtered = component.filteredGeofences();
    expect(filtered).toHaveLength(1);
    expect(filtered[0].status).toBe('pending_review');
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
        data: expect.objectContaining({ geofence: pendingGeofence }),
      }),
    );
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

    expect(adminGeofenceService.adminDelete).toHaveBeenCalledWith(geofence.id);
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

  it('should show snackbar on delete error', async () => {
    setup();
    component.ngOnInit();
    mockDialog.open.mockReturnValue({ afterClosed: () => of(true) });
    adminGeofenceService.adminDelete!.mockReturnValue(throwError(() => new Error('fail')));

    const geofence = component.allGeofences()[0];
    await component.adminDelete(geofence);

    expect(mockSnackBar.open).toHaveBeenCalledWith('ADMIN.SNACK_FAILED_DELETE_GEOFENCE', 'TOAST.OK', { duration: 3000 });
  });

  // ─── Geofence Reference Polygons ────────────────────────────────────────────

  it('should fetch reference geofences on init', () => {
    setup();
    component.ngOnInit();

    expect(areaService.getGeofencePolygons).toHaveBeenCalled();
  });

  // ─── Template Rendering (DOM) ────────────────────────────────────────────────

  it('should render status filter toggle buttons', () => {
    const fixture = setup();
    component.ngOnInit();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const toggleGroup = compiled.querySelector('mat-button-toggle-group');
    expect(toggleGroup).toBeTruthy();

    const filterToggles = compiled.querySelectorAll('.filter-tabs mat-button-toggle');
    expect(filterToggles.length).toBe(5); // all, pending_review, active, approved, rejected
  });

  it('should render geofence cards after loading', () => {
    const fixture = setup();
    component.ngOnInit();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const cards = compiled.querySelectorAll('.submission-card');
    expect(cards.length).toBe(4);
  });

  it('should display owner name in card when ownerName is available', () => {
    const fixture = setup();
    component.ngOnInit();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const ownerSpans = compiled.querySelectorAll('.submission-card-owner');
    const ownerTexts = Array.from(ownerSpans).map(s => s.textContent);
    expect(ownerTexts.some(t => t?.includes('UserOne'))).toBe(true);
  });

  it('should fall back to humanId in card when ownerName is not available', () => {
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

    const compiled = fixture.nativeElement as HTMLElement;
    const ownerSpan = compiled.querySelector('.submission-card-owner');
    expect(ownerSpan?.textContent).toContain('777');
  });

  it('should display group name in cards', () => {
    const fixture = setup();
    component.ngOnInit();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const groupSpans = compiled.querySelectorAll('.submission-card-group');
    const groupTexts = Array.from(groupSpans).map(s => s.textContent);
    expect(groupTexts.some(t => t?.includes('west end'))).toBe(true);
  });

  it('should show reviewer info in card meta for reviewed geofences', () => {
    const fixture = setup();
    component.ngOnInit();
    component.activeFilter.set('approved');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const metaItems = compiled.querySelectorAll('.meta-item');
    const reviewerItem = Array.from(metaItems).find(el => el.textContent?.includes('ADMIN.REVIEWED_BY'));
    expect(reviewerItem).toBeTruthy();
    expect(reviewerItem?.textContent).toContain('AdminReviewer');
  });

  it('should show review notes in card meta for reviewed geofences', () => {
    const fixture = setup();
    component.ngOnInit();
    component.activeFilter.set('rejected');
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const metaItems = compiled.querySelectorAll('.meta-item');
    const notesItem = Array.from(metaItems).find(el => el.textContent?.includes('Overlaps existing zone'));
    expect(notesItem).toBeTruthy();
  });

  it('should show Review button only for pending_review geofences', () => {
    const fixture = setup();
    component.ngOnInit();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const reviewButtons = compiled.querySelectorAll('button[color="primary"]');
    expect(reviewButtons).toHaveLength(1); // Only the pending_review geofence gets a Review button
  });

  it('should show empty state when no geofences match the filter', () => {
    const fixture = setup([]);
    component.ngOnInit();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const emptyState = compiled.querySelector('.empty-state');
    expect(emptyState).toBeTruthy();
  });

  it('should show skeleton cards while loading', () => {
    // Use NEVER to prevent getAll from completing, keeping loading=true
    const { NEVER } = require('rxjs');
    const fixture = setup();
    adminGeofenceService.getAll!.mockReturnValue(NEVER);
    component.ngOnInit();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const skeletons = compiled.querySelectorAll('.skeleton-card');
    expect(skeletons.length).toBe(3);
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

    const compiled = fixture.nativeElement as HTMLElement;
    const metaItems = compiled.querySelectorAll('.meta-item');
    const promotedItem = Array.from(metaItems).find(el => el.textContent?.includes('ADMIN.PROMOTED_AS'));
    expect(promotedItem).toBeTruthy();
    expect(promotedItem?.textContent).toContain('Official Park Area');
  });
});
