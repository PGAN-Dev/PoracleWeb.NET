import { TestBed } from '@angular/core/testing';

import { IconPackProbeService } from './icon-pack-probe.service';
import { ICON_REPO_PROBES } from '../../shared/utils/icon-repos';

/** A stand-in for the browser's Image, driven by which URLs the test says exist. */
class FakeImage {
  private value = '';
  naturalWidth = 0;
  onerror: (() => void) | null = null;

  onload: (() => void) | null = null;

  constructor(
    private readonly present: (url: string) => boolean,
    private readonly decodedWidth: number,
  ) {}

  get src(): string {
    return this.value;
  }

  set src(url: string) {
    this.value = url;
    if (!url) return;
    queueMicrotask(() => {
      if (this.present(url)) {
        this.naturalWidth = this.decodedWidth;
        this.onload?.();
      } else {
        this.onerror?.();
      }
    });
  }
}

class TestProbeService extends IconPackProbeService {
  /** What a loaded image decodes to. Zero stands for a host that answers 200 with an error page. */
  decodedWidth = 48;
  present: (url: string) => boolean = () => true;

  protected override createImage(): HTMLImageElement {
    return new FakeImage(url => this.present(url), this.decodedWidth) as unknown as HTMLImageElement;
  }
}

describe('IconPackProbeService', () => {
  let service: TestProbeService;

  beforeEach(() => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [TestProbeService] });
    service = TestBed.inject(TestProbeService);
  });

  it('reports nothing missing for a pack that has every category', async () => {
    expect(await service.probe('https://good.test/UICONS')).toEqual({ base: 'https://good.test/UICONS', missing: [] });
  });

  it('names the categories that did not load', async () => {
    service.present = url => !url.includes('/type/') && !url.includes('/invasion/');

    const result = await service.probe('https://partial.test/UICONS');

    expect(result.missing.sort()).toEqual(['uicons_invasion', 'uicons_type']);
  });

  it('reports every category missing for a host that answers nothing', async () => {
    // The whitewillem case: the repository is gone, so every request 404s.
    service.present = () => false;

    const result = await service.probe('https://deleted.test/UICONS');

    expect(result.missing).toHaveLength(ICON_REPO_PROBES.length);
  });

  it('normalizes the base before probing, so a trailing slash is not a failure', async () => {
    service.present = url => !url.includes('//UICONS') && url.includes('/UICONS/');

    expect((await service.probe('https://good.test/UICONS/')).base).toBe('https://good.test/UICONS');
  });

  it('counts a file that loads at zero width as missing', async () => {
    // Some hosts answer a missing path with a 200 and an error page. A decoded image of no size is
    // not artwork, and treating it as one would let a pack pass the check and render nothing.
    service.decodedWidth = 0;

    expect((await service.probe('https://liar.test/UICONS')).missing).toHaveLength(ICON_REPO_PROBES.length);
  });
});
