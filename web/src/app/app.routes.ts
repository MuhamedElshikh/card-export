import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'scan' },
  {
    path: 'scan',
    title: 'National ID Reader',
    loadComponent: () => import('./features/scan/scan.page').then((module) => module.ScanPage),
  },
  {
    path: 'records',
    title: 'Stored cards',
    loadComponent: () => import('./features/records/records.page').then((module) => module.RecordsPage),
  },
  { path: '**', redirectTo: 'scan' },
];
