import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { DemoService } from './demo.service';

describe('Demo configuration', () => {
  let http: HttpTestingController;
  let demo: DemoService;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    demo = TestBed.inject(DemoService);
  });
  afterEach(() => http.verify());
  it('keeps the normal application unchanged when disabled', async () => {
    const loading = demo.load();
    http.expectOne('/api/demo').flush({ enabled: false });
    await loading;
    expect(demo.enabled()).toBe(false);
    expect(demo.accounts()).toEqual([]);
    expect(demo.unavailable()).toBe(false);
  });
  it('loads only the two public accounts without storing unknown fields', async () => {
    const loading = demo.load();
    const accounts = (['User', 'Technician'] as const).map(role => ({ role, email: `${role}@demo.helpdesk.test`, password: crypto.randomUUID() }));
    http.expectOne('/api/demo').flush({ enabled: true, accounts });
    await loading;
    expect(demo.enabled()).toBe(true);
    expect(demo.accounts().map(a => a.role)).toEqual(['User', 'Technician']);
  });
  it.each([
    { enabled: true, accounts: [{ role: 'Admin' }] },
    { enabled: true, accounts: [] },
    { enabled: 'true' },
    null,
  ])('rejects malformed or administrative credentials', async value => {
    const loading = demo.load();
    http.expectOne('/api/demo').flush(value);
    await loading;
    expect(demo.enabled()).toBe(false);
    expect(demo.accounts()).toEqual([]);
    expect(demo.unavailable()).toBe(true);
  });
  it('does not retain public credentials after a failed refresh', async () => {
    const loading = demo.load();
    http.expectOne('/api/demo').flush({}, { status: 503, statusText: 'Unavailable' });
    await loading;
    expect(demo.accounts()).toEqual([]);
    expect(demo.unavailable()).toBe(true);
  });
});
