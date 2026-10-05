-- Migration: AddOrderPromoCode
-- Module 34 (lot 3) : code promo réellement appliqué, décision §5.5.O du
-- suivi. Porte le code validé au panier jusqu'à la commande, pour qu'il soit
-- rattaché (usage décompté) à la confirmation du paiement, jamais avant.
-- Idempotent : peut être rejoué sans casser une base déjà migrée.
--
-- Ordre de déploiement : ce script AVANT le code .NET de ce lot.

ALTER TABLE "Orders" ADD COLUMN IF NOT EXISTS "PromoCode" VARCHAR(50);
