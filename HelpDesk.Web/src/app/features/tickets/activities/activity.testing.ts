import { ActivityPage, TicketActivity } from './activity.service';
export const activityFixture = (changes: Partial<TicketActivity> = {}): TicketActivity => ({
  id: 100,
  ticketId: 41,
  actorUserId: 7,
  actorName: 'Historical admin',
  field: 'Priority',
  oldValue: 'Medium',
  newValue: 'High',
  oldAssigneeName: null,
  newAssigneeName: null,
  createdAt: '2026-11-04T09:00:00Z',
  ...changes,
});
export const activityPageFixture = (changes: Partial<ActivityPage> = {}): ActivityPage => ({
  items: [activityFixture()],
  hasMore: false,
  nextBeforeId: null,
  pageSize: 20,
  ...changes,
});
