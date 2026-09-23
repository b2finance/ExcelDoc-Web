import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, OnInit, ViewChild, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatDialog } from '@angular/material/dialog';
import { MatPaginator } from '@angular/material/paginator';
import { MatTableDataSource } from '@angular/material/table';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, switchMap, timer } from 'rxjs';
import { finalize, takeUntil } from 'rxjs/operators';
import { NotificationService } from '../../../../core/services/notification.service';
import { TranslateService } from '../../../../core/services/translate.service';
import { Processamento, ProcessamentoItem } from '../../models/processamento.model';
import { ProcessamentoService } from '../../services/processamento.service';
import { JsonDialogComponent, JsonDialogData } from '../json-dialog/json-dialog.component';

@Component({
  selector: 'app-processamento-detalhe',
  templateUrl: './processamento-detalhe.component.html',
  styleUrl: './processamento-detalhe.component.css'
})
export class ProcessamentoDetalheComponent implements OnInit {
  private static readonly processamentoStatusMap: Record<string, string> = {
    '1': 'Processando',
    '2': 'Sucesso',
    '3': 'Erro',
    Processando: 'Processando',
    Sucesso: 'Sucesso',
    Erro: 'Erro'
  };

  private static readonly itemStatusMap: Record<string, string> = {
    '1': 'Sucesso',
    '2': 'Erro',
    '3': 'Ignorado',
    Sucesso: 'Sucesso',
    Erro: 'Erro',
    Ignorado: 'Ignorado'
  };

  processamento: Processamento | null = null;
  readonly itensColumns: string[] = ['idExcel', 'linhaExcel', 'status', 'mensagem', 'acoes'];
  readonly itensDataSource = new MatTableDataSource<ProcessamentoItem>([]);
  readonly pageSizeOptions = [10, 25, 50];

  isLoading = false;
  isLoadingItens = false;

  private processamentoId!: number;
  private readonly destroyRef = inject(DestroyRef);
  private readonly stopPolling$ = new Subject<void>();

  @ViewChild(MatPaginator)
  set paginator(paginator: MatPaginator | undefined) {
    if (paginator) {
      this.itensDataSource.paginator = paginator;
      this.itensDataSource.paginator.pageSize = this.pageSizeOptions[0];
    }
  }

  constructor(
    private readonly route: ActivatedRoute,
    private readonly router: Router,
    private readonly dialog: MatDialog,
    private readonly processamentoService: ProcessamentoService,
    private readonly notificationService: NotificationService,
    private readonly translate: TranslateService
  ) { }

  ngOnInit(): void {
    this.processamentoId = Number(this.route.snapshot.paramMap.get('id'));
    this.loadProcessamento();
    this.loadItens();
    this.startPolling();
  }

  get isProcessando(): boolean {
    return this.getProcessamentoStatusValue(this.processamento?.status) === 'Processando';
  }

  get progressoPercentual(): number {
    if (!this.processamento || this.processamento.totalRegistros === 0) {
      return 0;
    }
    const processed = this.processamento.totalSucesso + this.processamento.totalErro + (this.processamento.totalIgnorado ?? 0);
    return Math.round((processed / this.processamento.totalRegistros) * 100);
  }

  voltar(): void {
    void this.router.navigate(['/processamento']);
  }

  reprocessar(): void {
    void this.router.navigate(['/processamento/upload']);
  }

  verJson(item: ProcessamentoItem): void {
    this.dialog.open(JsonDialogComponent, {
      width: '860px',
      maxWidth: 'calc(100vw - 32px)',
      data: {
        linhaExcel: item.linhaExcel,
        jsonEnviado: item.jsonEnviado,
        jsonRetorno: item.jsonRetorno
      } as JsonDialogData
    });
  }

  baixarErros(): void {
    const erros = this.itensDataSource.data.filter((i) => this.getItemStatusValue(i.status) === 'Erro');
    if (erros.length === 0) {
      this.notificationService.showInfo(this.translate.instant('processamento.processamentoDetalhe.feedback.info.noErrorRecords'));
      return;
    }

    const blob = new Blob([JSON.stringify(erros, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `erros-processamento-${this.processamentoId}.json`;
    a.click();
    URL.revokeObjectURL(url);
  }

  getProcessamentoStatusLabel(status: string | number | undefined): string {
    if (status === undefined || status === null) {
      return '—';
    }

    const value = this.getProcessamentoStatusValue(status);
    return this.translateStatus(value);
  }

  getProcessamentoStatusClass(status: string | number | undefined): string {
    return this.getProcessamentoStatusValue(status) === 'Erro'
      ? 'status-erro'
      : this.getProcessamentoStatusValue(status) === 'Sucesso'
        ? 'status-sucesso'
        : 'status-processando';
  }

  getItemStatusLabel(status: string | number | undefined): string {
    if (status === undefined || status === null) {
      return '—';
    }

    const value = this.getItemStatusValue(status);
    return this.translateStatus(value);
  }

  getItemStatusClass(status: string | number): string {
    const value = this.getItemStatusValue(status);
    if (value === 'Erro') {
      return 'status-erro';
    }

    return value === 'Ignorado' ? 'status-ignorado' : 'status-sucesso';
  }

  private loadProcessamento(): void {
    this.isLoading = true;
    this.processamentoService.getById(this.processamentoId)
      .pipe(
        finalize(() => { this.isLoading = false; }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (data) => { this.processamento = data; },
        error: (error: HttpErrorResponse) => {
          const msg = error.error?.detail ?? this.translate.instant('processamento.processamentoDetalhe.feedback.errors.loadProcessing');
          this.notificationService.showError(msg);
        }
      });
  }

  private loadItens(): void {
    this.isLoadingItens = true;
    this.processamentoService.getItens(this.processamentoId)
      .pipe(
        finalize(() => { this.isLoadingItens = false; }),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe({
        next: (data) => { this.itensDataSource.data = data.items; },
        error: (error: HttpErrorResponse) => {
          const msg = error.error?.detail ?? this.translate.instant('processamento.processamentoDetalhe.feedback.errors.loadItems');
          this.notificationService.showError(msg);
        }
      });
  }

  private startPolling(): void {
    timer(5000, 5000).pipe(
      takeUntil(this.stopPolling$),
      takeUntilDestroyed(this.destroyRef),
      switchMap(() => this.processamentoService.getById(this.processamentoId))
    ).subscribe({
      next: (data) => {
        this.processamento = data;
        if (this.getProcessamentoStatusValue(data.status) !== 'Processando') {
          this.stopPolling$.next();
          this.loadItens();
        }
      }
    });
  }

  private getProcessamentoStatusValue(status: string | number | undefined): string {
    if (status === undefined || status === null) {
      return '';
    }

    return ProcessamentoDetalheComponent.processamentoStatusMap[String(status)] ?? String(status);
  }

  private getItemStatusValue(status: string | number | undefined): string {
    if (status === undefined || status === null) {
      return '';
    }

    return ProcessamentoDetalheComponent.itemStatusMap[String(status)] ?? String(status);
  }

  private translateStatus(status: string): string {
    switch (status) {
      case 'Processando':
        return this.translate.instant('processamento.common.status.processing');
      case 'Sucesso':
        return this.translate.instant('processamento.common.status.success');
      case 'Erro':
        return this.translate.instant('processamento.common.status.error');
      case 'Ignorado':
        return this.translate.instant('processamento.common.status.ignored');
      default:
        return status;
    }
  }
}
