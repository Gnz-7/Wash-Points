import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'proveedor_mapas.dart';

/// Mapa de referencia sin proveedor real (D-09.9).
///
/// Pinta un lienzo relativo: el centro de búsqueda en el medio y cada lavadero
/// a una distancia proporcional a su distancia real, en un ángulo estable
/// derivado de su posición en la lista. No pretende ser geográficamente fiel;
/// sirve para que el flujo de búsqueda tenga una superficie visual sin
/// comprometerse con un proveedor.
class MapaPlaceholder implements ProveedorMapas {
  const MapaPlaceholder();

  @override
  Widget construir(
    BuildContext contexto, {
    required List<PuntoMapa> puntos,
    required double radioMetros,
    String? seleccionadoId,
  }) {
    final tema = Theme.of(contexto);

    return Container(
      key: const ValueKey('mapa_placeholder'),
      height: 180,
      margin: const EdgeInsets.fromLTRB(16, 8, 16, 12),
      decoration: BoxDecoration(
        color: tema.colorScheme.surfaceContainerHighest,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: tema.colorScheme.outlineVariant),
      ),
      clipBehavior: Clip.antiAlias,
      child: Stack(
        children: <Widget>[
          Positioned.fill(
            child: CustomPaint(
              painter: _PintorLienzo(
                puntos: puntos,
                radioMetros: radioMetros,
                seleccionadoId: seleccionadoId,
                colorGuia: tema.colorScheme.outlineVariant,
                colorCentro: tema.colorScheme.primary,
                colorPunto: tema.colorScheme.secondary,
                colorSeleccionado: tema.colorScheme.tertiary,
              ),
            ),
          ),
          Positioned(
            left: 12,
            bottom: 8,
            child: Text(
              'Mapa de referencia',
              style: tema.textTheme.labelSmall?.copyWith(
                color: tema.colorScheme.onSurfaceVariant,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _PintorLienzo extends CustomPainter {
  const _PintorLienzo({
    required this.puntos,
    required this.radioMetros,
    required this.seleccionadoId,
    required this.colorGuia,
    required this.colorCentro,
    required this.colorPunto,
    required this.colorSeleccionado,
  });

  final List<PuntoMapa> puntos;
  final double radioMetros;
  final String? seleccionadoId;
  final Color colorGuia;
  final Color colorCentro;
  final Color colorPunto;
  final Color colorSeleccionado;

  @override
  void paint(Canvas canvas, Size size) {
    final centro = Offset(size.width / 2, size.height / 2);
    final radioLienzo =
        math.min(size.width, size.height) / 2 - 16;

    if (radioLienzo <= 0) {
      return;
    }

    final guia = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1
      ..color = colorGuia;

    canvas.drawCircle(centro, radioLienzo, guia);

    canvas.drawCircle(
      centro,
      5,
      Paint()
        ..style = PaintingStyle.fill
        ..color = colorCentro,
    );

    if (puntos.isEmpty || radioMetros <= 0) {
      return;
    }

    for (var indice = 0; indice < puntos.length; indice++) {
      final punto = puntos[indice];
      final proporcion =
          (punto.distanciaMetros / radioMetros).clamp(0.0, 1.0);
      final angulo = (2 * math.pi * indice / puntos.length) - math.pi / 2;
      final posicion = centro +
          Offset(
            math.cos(angulo) * radioLienzo * proporcion,
            math.sin(angulo) * radioLienzo * proporcion,
          );

      final seleccionado = punto.id == seleccionadoId;
      canvas.drawCircle(
        posicion,
        seleccionado ? 7 : 5,
        Paint()
          ..style = PaintingStyle.fill
          ..color = seleccionado ? colorSeleccionado : colorPunto,
      );
    }
  }

  @override
  bool shouldRepaint(_PintorLienzo anterior) =>
      anterior.puntos != puntos ||
      anterior.radioMetros != radioMetros ||
      anterior.seleccionadoId != seleccionadoId;
}
