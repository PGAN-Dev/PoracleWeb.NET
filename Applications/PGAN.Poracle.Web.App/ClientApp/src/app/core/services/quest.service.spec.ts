import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConfigService } from './config.service';
import { QuestService } from './quest.service';
import { Quest, QuestCreate } from '../models';

describe('QuestService', () => {
  let service: QuestService;
  let httpMock: HttpTestingController;
  const API = 'http://test-api';

  const mockQuest: Quest = {
    clean: 0, distance: 0, id: '1', ping: null, pokemonId: 25,
    profileNo: 1, reward: 1, rewardType: 2, shiny: 0,
    template: null, uid: 1,
  };

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ConfigService, useValue: { apiHost: API } },
      ],
    });
    service = TestBed.inject(QuestService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('should fetch all quests', () => {
    service.getAll().subscribe(quests => {
      expect(quests).toHaveLength(1);
      expect(quests[0].pokemonId).toBe(25);
    });

    const req = httpMock.expectOne(`${API}/api/quests`);
    expect(req.request.method).toBe('GET');
    req.flush([mockQuest]);
  });

  it('should create a quest', () => {
    const payload: QuestCreate = {
      clean: 0, distance: 0, ping: null, pokemonId: 25,
      profileNo: 1, reward: 1, rewardType: 2, shiny: 1, template: null,
    };

    service.create(payload).subscribe(result => {
      expect(result.uid).toBe(1);
    });

    const req = httpMock.expectOne(`${API}/api/quests`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.shiny).toBe(1);
    req.flush(mockQuest);
  });

  it('should update a quest', () => {
    service.update(1, { shiny: 1 }).subscribe();

    const req = httpMock.expectOne(`${API}/api/quests/1`);
    expect(req.request.method).toBe('PUT');
    req.flush(null);
  });

  it('should delete a quest', () => {
    service.delete(3).subscribe();

    const req = httpMock.expectOne(`${API}/api/quests/3`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should delete all quests', () => {
    service.deleteAll().subscribe();

    const req = httpMock.expectOne(`${API}/api/quests`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null);
  });

  it('should update all distances', () => {
    service.updateAllDistance(2000).subscribe();

    const req = httpMock.expectOne(`${API}/api/quests/distance`);
    expect(req.request.method).toBe('PUT');
    req.flush(null);
  });
});
