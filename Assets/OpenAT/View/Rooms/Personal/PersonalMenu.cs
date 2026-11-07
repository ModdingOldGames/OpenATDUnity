using System.Collections.Generic;
using OpenAT.Sim;
using OpenAT.Sim.Staff;
using OpenAT.View.Render;
using OpenAT.View.UI;
using OpenAT.View.UI.Dialog;
using OpenAT.View.UI.Menus;
using UnityEngine;

namespace OpenAT.View.Rooms.Personal
{
    /// <summary>
    /// MENU_PERSONAL (rooms-and-ui-2 §4.10): the binder of applicants (hire mode) or own staff (staff
    /// mode) of one kind — advisors (any advisor type), pilots or flight attendants. Page 0 is the cover
    /// with count and average salary, page n shows worker n with hire / fire / raise / cut / assign plane.
    /// Every action is a staff command; the list is fixed when the binder opens, so a just-hired
    /// applicant keeps his page with the "hired" stamp.
    /// </summary>
    public sealed class PersonalMenu : PopupMenu
    {
        const string Lib = "gli/personal.gli";
        readonly int _type;
        readonly bool _hire;
        readonly List<int> _workers = new List<int>();
        int _page;
        float _repeatAt;
        int _repeatDir;
        bool _listed;

        /// <param name="type">Worker type: any advisor type (1..9) = all advisors, 10 pilots, 11 attendants.</param>
        /// <param name="hire">True: job market (applicants), false: own staff.</param>
        public PersonalMenu(int type, bool hire, int page = 0)
        {
            _type = WorkerType.IsAdvisor(type) ? 1 : type;
            _hire = hire;
            _page = page;
        }

        public override int Type => MenuIds.Personal;
        public override Vector2Int Size => new Vector2Int(261, 330);
        public override Vector2Int? FixedPos => new Vector2Int(189, 55);
        public override Texture Background => Assets.Texture(Lib, "ORDNER1");

        World W => App.Session.World;
        Player P => App.Session.Local;

        bool Matches(Worker wk) => _type == 1 ? wk.IsAdvisor : wk.Type == _type;

        public override void OnOpen()
        {
            if (_listed) return;
            _listed = true;
            var pool = W.Workers.Workers;
            for (int i = 0; i < pool.Count; i++)
            {
                var wk = pool[i];
                if (!Matches(wk)) continue;
                if (_hire ? wk.Employer == WorkerType.Jobless : wk.Employer == P.Index) _workers.Add(i);
            }
            _page = Mathf.Clamp(_page, 0, _workers.Count);
            App.Audio.Play("SOUND/pap3.raw");
        }

        int Pages => _workers.Count + 1;
        Worker Current => _page > 0 && _page <= _workers.Count ? W.Workers[_workers[_page - 1]] : null;
        int CurrentIndex => _page > 0 && _page <= _workers.Count ? _workers[_page - 1] : -1;

        void Flip(int dir)
        {
            int np = Mathf.Clamp(_page + dir, 0, Pages - 1);
            if (np == _page) return;
            _page = np;
            Paper();
            // The personnel advisor comments on each applicant (§4.10).
            if (_hire && CurrentIndex >= 0)
            {
                int id = P.ApplicantComment(CurrentIndex);
                var text = id > 0 ? Assets.StdText.Get("Advi", id) : null;
                if (text != null) App.Advisors.Queue(AdvisorKind.Personnel, DialogText.Format(text, new object[] { Current.Name }), AdvisorUrgency.Comment);
            }
        }

        public override void Update(InputState input, Vector2Int l)
        {
            var wk = Current;
            bool prev = _page > 0 && Hot(l, 0, 5, 35, 330, 0, CursorLook.Left);
            bool next = !prev && _page < Pages - 1 && Hot(l, 230, 302, 260, 330, 0, CursorLook.Right);
            bool hire = false, fire = false, raise = false, cut = false, assign = false;
            if (wk != null)
            {
                if (_hire && wk.Employer == WorkerType.Jobless) hire = Hot(l, 40, 254, 250, 268);
                if (!_hire && wk.Employer == P.Index)
                {
                    fire = Hot(l, 40, 256, 200, 268);
                    raise = Hot(l, 40, 269, 200, 281);
                    cut = Hot(l, 40, 282, 200, 295);
                }
                if (wk.Employer == P.Index && (wk.IsPilot || wk.IsAttendant)) assign = Hot(l, 40, 178, 260, 200);
            }

            // Holding the button on an arrow repeats: first after 900 ms, then every 300 ms.
            if (!input.LeftHeld) _repeatDir = 0;
            else if (_repeatDir != 0 && (prev || next) && Time.unscaledTime >= _repeatAt)
            {
                Flip(_repeatDir);
                _repeatAt = Time.unscaledTime + 0.3f;
            }
            if (!input.LeftDown) return;
            if (prev || next)
            {
                _repeatDir = prev ? -1 : 1;
                _repeatAt = Time.unscaledTime + 0.9f;
                Flip(_repeatDir);
                return;
            }
            var w = W;
            int me = P.Index, idx = CurrentIndex;
            if (hire) { if (w.Execute(new HireWorkerCommand(me, idx)).Ok) App.Audio.Play("SOUND/paptake.raw"); }
            else if (fire) w.Execute(new FireWorkerCommand(me, idx));
            else if (raise) w.Execute(new ChangeSalaryCommand(me, idx, true));
            else if (cut) w.Execute(new ChangeSalaryCommand(me, idx, false));
            else if (assign) OpenPlaneJob(idx);
        }

        /// <summary>MENU_PLANEJOB (§4.11): pick the plane the crew member works on; the binder reopens on the same page.</summary>
        void OpenPlaneJob(int worker)
        {
            var host = Host;
            int page = _page;
            var list = new NotepadListMenu(MenuIds.PlaneJob);
            var planes = W.OperationsBridge.Planes(W, P);
            foreach (var pl in planes) list.Rows.Add(new NotepadListMenu.Row { Name = pl.Name, Tag = pl.Id });
            list.Title0 = Std("Jobs", 1102);
            var me = P.Index;
            var reopen = new PersonalMenu(_type, _hire, page);
            reopen._listed = true;
            reopen._workers.AddRange(_workers);
            list.Picked = (i, row) => W.Execute(new AssignWorkerCommand(me, worker, (int)row.Tag));
            list.Closed = () => { if (host.Current == null) host.Open(reopen); };
            Close();
            host.Open(list);
        }

        public override void OnRightClick() { App.Audio.Play("SOUND/pap2.raw"); Close(); }

        public override void Draw(Screen2D s, int x, int y, Vector2Int l)
        {
            s.Draw(Background, x, y);
            var f = Kit.SmallBlack;
            var wk = Current;
            if (wk == null)
            {
                DrawCover(s, x, y, f);
            }
            else
            {
                s.Draw(Assets.Texture(Lib, "ORDNER2"), x, y);
                DrawWorker(s, x, y, f, wk, l);
            }
            if (_page < Pages - 1) s.Draw(Assets.Texture(Lib, "ORDNER3"), x + 225, y + 294);
        }

        void DrawCover(Screen2D s, int x, int y, BitmapFont f)
        {
            Assets.Font("inkfont.mcf").Draw(s, x + 145, y + 38, Std("Jobs", 1000 + (_type == 1 ? 0 : _type)), TextAlign.Center);
            f.Draw(s, x + 145, y + 70, DialogText.Format(Std("Jobs", _hire ? 5001 : 5000), new object[] { (long)_workers.Count }), TextAlign.Center);
            if (_workers.Count == 0) return;
            long sum = 0;
            foreach (int i in _workers) sum += W.Workers[i].Salary;
            f.Draw(s, x + 145, y + 88, DialogText.Format(Std("Jobs", 5010), new object[] { sum / _workers.Count }), TextAlign.Center);
        }

        void DrawWorker(Screen2D s, int x, int y, BitmapFont f, Worker wk, Vector2Int l)
        {
            int tx = x + 40;
            Assets.Font("inkfont.mcf").Draw(s, tx, y + 10, wk.Name ?? "");
            f.Draw(s, tx, y + 80, Std("Jobs", 2000 + wk.Type + 1000 * wk.Sex));
            f.Draw(s, tx, y + 93, DialogText.Format(Std("Jobs", 5050), new object[] { (long)wk.Salary }));
            f.Draw(s, tx, y + 106, Std("Jobs", 5020 + Mathf.Clamp(wk.Talent / 10, 0, 10)));
            f.Draw(s, tx, y + 119, DialogText.Format(Std("Jobs", 1105), new object[] { (long)wk.Age }));
            bool mine = wk.Employer == P.Index;
            if (mine)
            {
                f.Draw(s, tx, y + 132, DialogText.Format(Std("Jobs", 5055), new object[] { (long)Mathf.Clamp((wk.Happiness + 100) / 2, 0, 100) }));
                if (wk.IsPilot || wk.IsAttendant)
                {
                    string plane = null;
                    if (wk.PlaneId != -1)
                        foreach (var pl in W.OperationsBridge.Planes(W, P)) if (pl.Id == wk.PlaneId) plane = pl.Name;
                    f.DrawBlock(s, new RectInt(tx, y + 155, 210, 24), plane == null ? Std("Jobs", 1100) : DialogText.Format(Std("Jobs", 1101), new object[] { plane }), Color.white);
                    Band(s, x, y, l, 40, 178, 260, 200);
                    f.Draw(s, tx, y + 181, Std("Jobs", 1102));
                }
            }
            if (!mine && wk.Employer == WorkerType.Jobless)
            {
                if (!string.IsNullOrEmpty(wk.Comment)) f.DrawBlock(s, new RectInt(tx, y + 184, 205, 66), wk.Comment, Color.white);
                if (_hire) { Band(s, x, y, l, 40, 254, 250, 268); f.Draw(s, tx, y + 258, Std("Jobs", 5060)); }
            }
            else if (mine && _hire)
            {
                f.Draw(s, tx, y + 258, Std("Jobs", 5064));
                s.Draw(Assets.Texture(Lib, "KREUZLE"), x + 216, y + 235);
            }
            else if (mine)
            {
                Band(s, x, y, l, 40, 256, 200, 268); f.Draw(s, tx, y + 258, Std("Jobs", 5061));
                Band(s, x, y, l, 40, 269, 200, 281); f.Draw(s, tx, y + 271, DialogText.Format(Std("Jobs", 5062), System.Array.Empty<object>()));
                Band(s, x, y, l, 40, 282, 200, 295); f.Draw(s, tx, y + 284, DialogText.Format(Std("Jobs", 5063), System.Array.Empty<object>()));
            }
            f.Draw(s, x + 145, y + 306, DialogText.Format(Std("Jobs", 1020), new object[] { (long)_page, (long)(Pages - 1) }), TextAlign.Center);
        }

        static void Band(Screen2D s, int x, int y, Vector2Int l, int x1, int y1, int x2, int y2)
        {
            if (l.x >= x1 && l.x <= x2 && l.y >= y1 && l.y <= y2) HoverBand(s, new RectInt(x + x1, y + y1, x2 - x1, y2 - y1 + 1));
        }
    }
}
