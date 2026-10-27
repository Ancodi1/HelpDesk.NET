import { TestBed } from '@angular/core/testing';
import { TicketTable } from './ticket-table';
import { ticketFixture } from './ticket.testing';

describe('Responsive ticket presentation', () => {
  it('renders real fields and relation IDs without invented names', () => {
    const f = TestBed.createComponent(TicketTable);
    f.componentRef.setInput('tickets', [ticketFixture({ assignedToUserId: 7 })]);
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('ID 7');
    expect(f.nativeElement.textContent).toContain('Abierta');
    expect(f.nativeElement.textContent).toContain('Media');
    expect(f.nativeElement.textContent).toContain('Software');
    expect(f.nativeElement.querySelector('time').getAttribute('datetime')).toBe(
      '2026-10-27T10:00:00Z',
    );
    expect(f.nativeElement.querySelectorAll('th[scope="col"]').length).toBe(6);
  });
  it('escapes untrusted ticket text rather than inserting HTML', () => {
    const f = TestBed.createComponent(TicketTable);
    f.componentRef.setInput('tickets', [
      ticketFixture({ title: '<img src=x>', description: '<script>test</script>' }),
    ]);
    f.detectChanges();
    expect(f.nativeElement.querySelector('img')).toBeNull();
    expect(f.nativeElement.querySelector('script')).toBeNull();
    expect(f.nativeElement.textContent).toContain('<img src=x>');
  });
});
