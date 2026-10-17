import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';

import { AdminUsersComponent } from './admin-users.component';
import { AdminUser, PoracleServerConfig } from '../../core/models';
import { AdminService } from '../../core/services/admin.service';
import { AuthService } from '../../core/services/auth.service';
import { PoracleConfigService } from '../../core/services/poracle-config.service';
import { GameAccountsDialogComponent } from '../../shared/components/game-accounts-dialog/game-accounts-dialog.component';

// The avatar cell observes its own visibility; jsdom has no IntersectionObserver.
global.IntersectionObserver = jest.fn().mockImplementation(() => ({
  disconnect: jest.fn(),
  observe: jest.fn(),
  unobserve: jest.fn(),
}));

describe('AdminUsersComponent game accounts', () => {
  const user = { id: '1234', name: 'Ash', adminDisable: 0, currentProfileNo: 1, enabled: 1, type: 'discord:user' } as AdminUser;
  const unnamed = { ...user, id: '5678', name: null } as AdminUser;

  const setup = (config: Partial<PoracleServerConfig>) => {
    const dialog = { open: jest.fn() };
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        { provide: AdminService, useValue: { getUsers: () => of([user, unnamed]) } },
        { provide: AuthService, useValue: { isAdmin: () => true } },
        { provide: PoracleConfigService, useValue: { load: () => of(config), serverConfig: signal(config) } },
      ],
      imports: [AdminUsersComponent, NoopAnimationsModule],
    });
    TestBed.overrideProvider(MatDialog, { useValue: dialog });
    const fixture = TestBed.createComponent(AdminUsersComponent);
    fixture.detectChanges();
    return { dialog, fixture, page: fixture.nativeElement as HTMLElement };
  };

  it('offers a Game accounts button on each row when PoracleNG supports them', () => {
    const { page } = setup({ playerAccountsSupported: true });

    expect(page.querySelectorAll('.game-accounts-button')).toHaveLength(2);
  });

  it('offers none on a PoracleNG without game accounts', () => {
    const { page } = setup({ playerAccountsSupported: false });

    expect(page.querySelector('.game-accounts-button')).toBeNull();
  });

  it("opens the dialog for the row's user, falling back to the id when there is no name", () => {
    const { dialog, fixture } = setup({ playerAccountsSupported: true });
    const sut = fixture.componentInstance;

    sut.openGameAccounts(user);
    sut.openGameAccounts(unnamed);

    expect(dialog.open).toHaveBeenNthCalledWith(
      1,
      GameAccountsDialogComponent,
      expect.objectContaining({ data: { displayName: 'Ash', userId: '1234' } }),
    );
    expect(dialog.open).toHaveBeenNthCalledWith(
      2,
      GameAccountsDialogComponent,
      expect.objectContaining({ data: { displayName: '5678', userId: '5678' } }),
    );
  });
});
