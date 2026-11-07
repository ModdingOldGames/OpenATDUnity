# Game mechanics reference (free game)

How the free game of Airline Tycoon Deluxe works, with formulas and constants, and how far OpenAT
implements each rule. Written from our clean-room specs (`docs/spec/findings/`) and checked against the
original's behaviour; all text is our own. Per-type data (plane types, routes, cities, staff tables)
is not reproduced here: it is read from the install at run time. A local, gitignored table of all
plane types can be generated into `docs/spec/local/mechanics/plane-types.md` (see `planes.md`).

Each document ends its sections with an **Implementation in OpenAT** list (Implemented / Partial /
Missing, with file references) and ends with a **Guide claims** table that checks the statements of
a popular third-party strategy guide (GameFAQs, "Polar", written for the older editions) against
Deluxe.

| Document | Covers |
|---|---|
| [economy.md](economy.md) | ticket and freight income, contract payments and penalties, fuel use, salaries, upkeep, rents, kerosene market, tanks and fuel quality, bank interest, day/month money events, bankruptcy, balance sheet |
| [image-and-advertising.md](image-and-advertising.md) | airline and route image, what raises and lowers it, decay, ad campaigns (types, cost, effect, duration), passenger moods and thought bubbles |
| [routes-and-orders.md](routes-and-orders.md) | route rental, demand and the passenger split between airlines, minimum-use rule, route advisor; travel / last-minute / freight orders; gates, airport expansion, branch auctions, cooperation, home airport |
| [planes.md](planes.md) | plane type fields and what they drive, broker availability, condition and wear, breakdowns and incidents, equipment, food, repairs and mechanics, museum planes, selling, designer planes, plane advisor |
| [staff-and-advisors.md](staff-and-advisors.md) | applicant pool, salaries, hiring and firing, mood, strikes, crew requirements, every advisor and its skill thresholds |
| [sabotage-security-items.md](sabotage-security-items.md) | saboteur access, all acts with price and evidence, being caught and fines, computer-player sabotage, security office, every item and trade chain, laptop battery and virus, cooperation |
| [finance-shares-valuation.md](finance-shares-valuation.md) | loans and credit limit, share price model, share issues, trading, dividends, takeovers, company value, ranking, end conditions |
| [computer-players.md](computer-players.md) | how the computer airlines play and react to the player |

## Status of the gaps (October 2026)

The gaps found while writing these documents were fixed on 2026-10-24: bank share trading, dividend
form and takeovers; all item trade chains; route advisor, partner branches, order analyst, contract
cancel, stink-bomb image loss; fitness trainer, crew pay per plane, strike fax, random advisor and
informer messages, fuel grade choice, the drinker at Rick's; planner balance panes and highscores;
hotkey languages, key gating, the two-hour room stay limit; and the original's bugs, which are now
fixed by default (`docs/QUIRKS.md` lists every switch that restores the original). The
"Implementation in OpenAT" lists in each document carry the current state.

Still open:
- Darkened office door art in the hall after a rival's power was cut.
- The four figures leaving the lift together at game start (not covered by the specs).
- Chocolates for network rivals (no network play).
- The original's sound mix silencing the two plane loops inside rooms after a volume change.
