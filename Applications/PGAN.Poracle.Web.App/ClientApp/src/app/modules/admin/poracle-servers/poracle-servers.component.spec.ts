import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { of, throwError } from 'rxjs';

import { PoracleServerStatus } from '../../../core/models';
import { AdminService } from '../../../core/services/admin.service';
import { ConfigService } from '../../../core/services/config.service';
import { PoracleServersComponent } from './poracle-servers.component';

describe('PoracleServersComponent', () => {
  let component: PoracleServersComponent;
  let adminService: { [K in keyof AdminService]?: jest.Mock };
  let mockDialog: { open: jest.Mock };
  let mockSnackBar: { open: jest.Mock };

  const mockServers: PoracleServerStatus[] = [
    { checkedAt: '2026-03-21T00:00:00Z', host: '10.0.0.1', name: 'Server1', online: true },
    { checkedAt: '2026-03-21T00:00:00Z', host: '10.0.0.2', name: 'Server2', online: false },
  ];

  beforeEach(() => {
    adminService = {
      getPoracleServers: jest.fn().mockReturnValue(of(mockServers)),
      restartAllServers: jest.fn().mockReturnValue(of(mockServers)),
      restartServer: jest.fn().mockReturnValue(of(mockServers[0])),
    };

    mockDialog = {
      open: jest.fn().mockReturnValue({ afterClosed: () => of(true) }),
    };

    mockSnackBar = {
      open: jest.fn(),
    };

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      imports: [NoopAnimationsModule],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AdminService, useValue: adminService },
        { provide: ConfigService, useValue: { apiHost: 'http://test-api' } },
      ],
    })
      .overrideComponent(PoracleServersComponent, {
        set: {
          providers: [
            { provide: MatDialog, useValue: mockDialog },
            { provide: MatSnackBar, useValue: mockSnackBar },
          ],
        },
      })
      .compileComponents();

    const fixture = TestBed.createComponent(PoracleServersComponent);
    component = fixture.componentInstance;
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('should load servers on init', () => {
    component.ngOnInit();

    expect(adminService.getPoracleServers).toHaveBeenCalled();
    expect(component.servers()).toEqual(mockServers);
    expect(component.loading()).toBe(false);
  });

  it('should refresh servers when refreshServers is called', () => {
    component.refreshServers();

    expect(adminService.getPoracleServers).toHaveBeenCalled();
    expect(component.servers()).toEqual(mockServers);
  });

  it('should restart a single server after confirmation', async () => {
    component.ngOnInit();

    await component.restartServer(mockServers[0]);

    expect(mockDialog.open).toHaveBeenCalled();
    expect(adminService.restartServer).toHaveBeenCalledWith('10.0.0.1');
  });

  it('should not restart server when dialog is cancelled', async () => {
    mockDialog.open.mockReturnValue({ afterClosed: () => of(false) });

    await component.restartServer(mockServers[0]);

    expect(adminService.restartServer).not.toHaveBeenCalled();
  });

  it('should restart all servers after confirmation', async () => {
    component.ngOnInit();

    await component.restartAll();

    expect(mockDialog.open).toHaveBeenCalled();
    expect(adminService.restartAllServers).toHaveBeenCalled();
  });

  it('should not restart all when dialog is cancelled', async () => {
    mockDialog.open.mockReturnValue({ afterClosed: () => of(false) });

    await component.restartAll();

    expect(adminService.restartAllServers).not.toHaveBeenCalled();
  });

  it('should show snackbar on load error', () => {
    adminService.getPoracleServers!.mockReturnValue(throwError(() => new Error('fail')));

    component.ngOnInit();

    expect(component.loading()).toBe(false);
    expect(mockSnackBar.open).toHaveBeenCalledWith('Failed to load Poracle servers', 'OK', { duration: 3000 });
  });

  it('should update server in list after individual restart', async () => {
    component.ngOnInit();

    const updatedServer: PoracleServerStatus = {
      checkedAt: '2026-03-21T01:00:00Z',
      host: '10.0.0.1',
      message: 'pm2 restarted',
      name: 'Server1',
      online: true,
    };
    adminService.restartServer!.mockReturnValue(of(updatedServer));

    await component.restartServer(mockServers[0]);

    const servers = component.servers();
    expect(servers[0].message).toBe('pm2 restarted');
    expect(servers[1]).toEqual(mockServers[1]);
  });

  it('should set restartingAll flag during restart all', async () => {
    component.ngOnInit();

    await component.restartAll();

    expect(component.restartingAll()).toBe(false);
  });
});
