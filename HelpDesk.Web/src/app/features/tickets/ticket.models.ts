export const ticketStatuses = ['Open', 'InProgress', 'Resolved', 'Closed'] as const;
export const ticketPriorities = ['Low', 'Medium', 'High', 'Critical'] as const;
export const ticketCategories = ['Hardware', 'Software', 'Network', 'Access', 'Other'] as const;
export type TicketStatus = (typeof ticketStatuses)[number];
export type TicketPriority = (typeof ticketPriorities)[number];
export type TicketCategory = (typeof ticketCategories)[number];

export interface Ticket {
  id: number;
  title: string;
  description: string;
  createdAt: string;
  status: TicketStatus;
  priority: TicketPriority;
  category: TicketCategory;
  createdByUserId: number | null;
  assignedToUserId: number | null;
}
export interface TicketPage {
  items: Ticket[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}
export interface TicketQuery {
  search: string;
  status: TicketStatus | '';
  priority: TicketPriority | '';
  category: TicketCategory | '';
  page: number;
  pageSize: number;
}
export const statusLabels: Record<TicketStatus, string> = {
  Open: 'Abierta',
  InProgress: 'En curso',
  Resolved: 'Resuelta',
  Closed: 'Cerrada',
};
export const priorityLabels: Record<TicketPriority, string> = {
  Low: 'Baja',
  Medium: 'Media',
  High: 'Alta',
  Critical: 'Crítica',
};
export const categoryLabels: Record<TicketCategory, string> = {
  Hardware: 'Hardware',
  Software: 'Software',
  Network: 'Red',
  Access: 'Acceso',
  Other: 'Otra',
};

// Writable fields: creator, status, ID and date are always decided by the API.
export interface CreateTicketRequest {
  title: string;
  description: string;
  priority: TicketPriority;
  category: TicketCategory;
}
