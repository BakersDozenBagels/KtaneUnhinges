using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using KModkit;
using UnityEngine;
using Random = UnityEngine.Random;

public class UnhingesScript : MonoBehaviour
{
    [SerializeField]
    private Texture[] _cracks;
    [SerializeField]
    private Renderer _backing;
    [SerializeField]
    private TextMesh _colorblindText;

    private int _id = ++_idc, _mistakeMode, _lastSelection, _lastForce;
    private static int _idc;
    private static readonly string Alpha = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private bool _isSolved, _activated, _cbOn;
    private KMBombInfo _info;
    private Maze _maze;

    private int _x, _y, _tx, _ty, _ox, _oy;

    private void Start()
    {
        SetCB(GetComponent<KMColorblindMode>().ColorblindModeActive);

        _info = GetComponent<KMBombInfo>();
        var valid = _info.GetModuleNames().Where(s => Alpha.Contains(s.ToUpperInvariant()[0]) && !s.Equals("Unhinges")).ToList();
        if(valid.Count < 4)
        {
            Log("There are too few modules. This is a mistake.");
            _mistakeMode = 1;
            GetComponent<KMSelectable>().Children[0].OnInteract += () => { Mistake(); return false; };
            return;
        }

        var seed = GetComponent<KMRuleSeedable>();
        Log("Using rule seed " + seed.GetRNG().Seed);
        _maze = Maze.FromRuleSeed(seed);
        Type t = ReflectionHelper.FindTypeInGame("BombComponent");
        Type t2 = ReflectionHelper.FindTypeInGame("Selectable");
        FieldInfo fi = t.GetField("ComponentType", ReflectionHelper.Flags);
        FieldInfo fi2 = t2.GetField("OnFocus", ReflectionHelper.Flags);
        var allModules = transform.root.GetComponentsInChildren(t).Where(c => !new int[] { 0, 1 }.Contains((int)fi.GetValue(c)) && c != GetComponent(t));

        var chosenModules = allModules.Where(c =>
            !new int[] { 16, 17 }.Contains((int)fi.GetValue(c)) ||
            (int)fi.GetValue(c) == 16 && Alpha.Contains(c.GetComponent<KMBombModule>().ModuleDisplayName.ToUpperInvariant()[0]) ||
            (int)fi.GetValue(c) == 17 && Alpha.Contains(c.GetComponent<KMNeedyModule>().ModuleDisplayName.ToUpperInvariant()[0])
        ).OrderBy(_ => Random.value).Take(4).ToArray();

        foreach(var sel in allModules.Except(chosenModules).Select(m => m.GetComponent(t2)))
            fi2.SetValue(sel, Delegate.Combine((Action)fi2.GetValue(sel), (Action)delegate () { Selected(-1); }));

        string[] coords = new string[4];
        for(int i = 0; i < 4; i++)
        {
            int j = i;
            fi2.SetValue(chosenModules[i].GetComponent(t2), Delegate.Combine((Action)fi2.GetValue(chosenModules[i].GetComponent(t2)), (Action)delegate () { Selected(j); }));
            if((int)fi.GetValue(chosenModules[i]) == 16)
                coords[i] = chosenModules[i].GetComponent<KMBombModule>().ModuleDisplayName;
            else if((int)fi.GetValue(chosenModules[i]) == 17)
                coords[i] = chosenModules[i].GetComponent<KMNeedyModule>().ModuleDisplayName;
            else
            {
                var e = t.Field<object>("ComponentType", chosenModules[i]);
                coords[i] = (string)e.GetType().GetMethods(ReflectionHelper.Flags).First(m => m.Name == "ToString" && m.GetParameters().Length == 0).Invoke(e, new object[0]);
                if(coords[i].StartsWith("Needy"))
                    coords[i] = coords[i].Substring(5);
            }
        }

        Log("Red = " + coords[0]);
        Log("Green = " + coords[3]);
        Log("Blue = " + coords[1]);
        Log("Black = " + coords[2]);

        _oy = _y = Alpha.IndexOf(coords[0].ToUpperInvariant()[0]) % 13;
        _ox = _x = Alpha.IndexOf(coords[3].ToUpperInvariant()[0]) % 13;
        _ty = Alpha.IndexOf(coords[1].ToUpperInvariant()[0]) % 13;
        _tx = Alpha.IndexOf(coords[2].ToUpperInvariant()[0]) % 13;

        Log("Starting at (" + _x + "," + _y + ")");
        Log("Going to (" + _tx + "," + _ty + ")");

        GetComponent<KMSelectable>().OnFocus += Force;
    }

    private void SetCB(bool b)
    {
        _cbOn = b;
        _colorblindText.gameObject.SetActive(b);
        _colorblindText.text = "";
        _colorblindText.color = new Color(1f, 1f, 1f, 0f);
    }

    private void Force()
    {
        if(_lastSelection == -1 || _mistakeMode != 0 || _isSolved)
            return;

        Log("Forced " + (new string[] { "red", "blue", "black", "green" }[_lastSelection]) + ".");
        StartCoroutine(Animate(_lastSelection));

        if(Mathf.Abs(_lastForce - _lastSelection) == 2 && _lastForce != -1 && _lastSelection != -1)
        {
            if(_lastForce == 2)
            {
                Log("Module Activated.");
                _activated = true;
            }
            else if(_lastForce == 0)
            {
                Log("Position reset.");
                _activated = false;
                _x = _ox;
                _y = _oy;
            }
            else if(_lastForce == 3 && _activated)
            {
                if(_x == _tx && _y == _ty)
                {
                    Log("Module solved!");
                    GetComponent<KMBombModule>().HandlePass();
                    _isSolved = true;
                }
                else
                {
                    Log("That's not the correct position to submit. Strike!");
                    GetComponent<KMBombModule>().HandleStrike();
                }
            }

            _lastSelection = -1;
        }
        else if(_activated && _lastForce >= 0 && _lastForce <= 3)
        {
            Log("Moving " + (new string[] { "up", "right", "down", "left" }[_lastForce]) + ".");
            if(_maze.AvailableMoves(_x, _y)[_lastForce])
            {
                switch(_lastForce)
                {
                    case 0:
                        _y--;
                        break;
                    case 1:
                        _x++;
                        break;
                    case 2:
                        _y++;
                        break;
                    case 3:
                        _x--;
                        break;
                }
                Log("You're now at (" + _x + "," + _y + ")");
            }
            else
            {
                Log("There's a wall there. Strike!");
                GetComponent<KMBombModule>().HandleStrike();
            }
        }

        _lastForce = _lastSelection;
        _lastSelection = -1;
    }

    private IEnumerator Animate(int i)
    {
        GetComponent<KMAudio>().PlaySoundAtTransform("Force", transform);
        _colorblindText.text = new string[] { "RED", "BLUE", "BLACK", "GREEN" }[i];
        Color c = new Color[] { new Color(1f, 0f, 0f), new Color(0f, 0f, 1f), new Color(0f, 0f, 0f), new Color(0f, 1f, 0f) }[i];
        float t = Time.time;
        while(Time.time - t < 1f)
        {
            _backing.material.color = Color.Lerp(Color.white, c, Time.time - t);
            _colorblindText.color = Color.Lerp(new Color(1f, 1f, 1f, 0f), Color.white, Time.time - t);
            yield return null;
        }
        t = Time.time;
        while(Time.time - t < 1f)
        {
            _backing.material.color = Color.Lerp(c, Color.white, Time.time - t);
            _colorblindText.color = Color.Lerp(Color.white, new Color(1f, 1f, 1f, 0f), Time.time - t);
            yield return null;
        }
        _backing.material.color = Color.white;
        _colorblindText.color = new Color(1f, 1f, 1f, 0f);
        _colorblindText.text = "";
    }

    private void Selected(int v)
    {
        _lastSelection = v;
    }

    private void Mistake()
    {
        if(_mistakeMode == 0 || _isSolved)
            return;

        bool flag = false;
        switch(_mistakeMode)
        {
            case 1:
                flag = true;
                Log("Pressed once.");
                break;
            case 2:
                flag = _info.GetSerialNumberNumbers().Last() == (int)(_info.GetTime() % 10);
                Log("Pressed twice at " + (int)(_info.GetTime() % 10) + ".");
                break;
            case 3:
                flag = _info.GetSerialNumberNumbers().Sum() == (int)(_info.GetTime() % 60);
                Log("Pressed thrice at " + (int)(_info.GetTime() % 60) + ".");
                break;
        }

        GetComponent<KMAudio>().PlaySoundAtTransform("MistakeShatter", transform);
        _backing.material.mainTexture = _cracks[_mistakeMode - 1];

        if(!flag)
            GetComponent<KMBombModule>().HandleStrike();
        _mistakeMode++;
        if(_mistakeMode == 4)
        {
            GetComponent<KMBombModule>().HandlePass();
            Log("Solved.");
            _isSolved = true;
        }
    }

    private void Log(string v)
    {
        Debug.Log("[Unhinges #" + _id + "] " + v);
    }

    private class Maze
    {
        private int _x, _y;
        private bool[] _hwalls, _vwalls;

        private Maze(Vector2Int size, bool[] hwalls, bool[] vwalls)
        {
            if(hwalls.Length != size.x * size.y || vwalls.Length != size.y * size.x)
                throw new ArgumentException("Unequal maze size!");
            _hwalls = hwalls;
            _vwalls = vwalls;
            _x = size.x;
            _y = size.y;
        }

        public bool[] AvailableMoves(int x, int y)
        {
            if(x >= _x || x < 0 || y >= _y || y < 0)
                throw new ArgumentException("Bad position");
            return new bool[]
            {
                y == 0 ? false : !_hwalls[x + y * _x],
                x == _x - 1 ? false : !_vwalls[(x + 1) + y * _x],
                y == _y - 1 ? false : !_hwalls[x + (y + 1) * _x],
                x == 0 ? false : !_vwalls[x + y * _x]
            };
        }

        public static Maze FromRuleSeed(KMRuleSeedable seed)
        {
            var rng = seed.GetRNG();

            var hwalls = Enumerable.Range(0, 169).ToList();
            var vwalls = Enumerable.Range(0, 169).ToList();

            var active = new List<int>();
            var todo = Enumerable.Range(0, 169).ToList();
            var start = rng.Next(0, todo.Count);
            active.Add(todo[start]);
            todo.RemoveAt(start);

            while(todo.Count > 0)
            {
                var activeIx = rng.Next(0, active.Count);
                if(active.Count == 0)
                    Debug.Log("The j");
                var sq = active[activeIx];

                var adjs = new List<int>();
                if((sq % 13) > 0 && todo.Contains(sq - 1))
                    adjs.Add(sq - 1);
                if((sq % 13) < 12 && todo.Contains(sq + 1))
                    adjs.Add(sq + 1);
                if((sq / 13) > 0 && todo.Contains(sq - 13))
                    adjs.Add(sq - 13);
                if((sq / 13) < 12 && todo.Contains(sq + 13))
                    adjs.Add(sq + 13);

                if(adjs.Count == 0)
                {
                    active.RemoveAt(activeIx);
                    continue;
                }

                var adj = adjs[rng.Next(0, adjs.Count)];
                todo.RemoveAt(todo.IndexOf(adj));
                active.Add(adj);

                if(adj == sq - 1 && vwalls.Contains(sq))
                    vwalls.RemoveAt(vwalls.IndexOf(sq));
                else if(adj == sq + 1 && vwalls.Contains(adj))
                    vwalls.RemoveAt(vwalls.IndexOf(adj));
                else if(adj == sq - 13 && hwalls.Contains(sq))
                    hwalls.RemoveAt(hwalls.IndexOf(sq));
                else if(adj == sq + 13 && hwalls.Contains(adj))
                    hwalls.RemoveAt(hwalls.IndexOf(adj));
            }

            for(var x = 0; x < 3; x++)
            {
                for(var y = 0; y < 3; y++)
                {
                    var unvisited = Enumerable.Range(4 * y, 5).SelectMany(row => Enumerable.Range(13 * row + 4 * x, 5)).ToList();
                    var adjs = new List<int>();
                    var potentials = new List<Node>();
                    adjs.Add(unvisited[rng.Next(0, unvisited.Count)]);

                    while(unvisited.Count > 0)
                    {
                        if(adjs.Count == 0)
                        {
                            // for (let i = potentials.length - 1; i >= 0; i--)
                            //     if (!(potentials[i].t == "h" ? hwalls : vwalls).includes(potentials[i].p))
                            //         potentials.splice(i, 1);

                            if(potentials.Count == 0)
                            {
                                Debug.Log("aaaaaa screaming i don't like this (infloop maybe?)");
                                break;
                            }
                            var selection = potentials[rng.Next(0, potentials.Count)];
                            adjs.Add(selection.c);

                            if(selection.t == 'h')
                                hwalls.RemoveAt(hwalls.IndexOf(selection.p));
                            else
                                vwalls.RemoveAt(vwalls.IndexOf(selection.p));
                        }

                        var current = adjs[rng.Next(0, adjs.Count)];

                        unvisited.RemoveAt(unvisited.IndexOf(current));
                        adjs.RemoveAt(adjs.IndexOf(current));

                        for(var i = potentials.Count - 1; i >= 0; i--)
                            if(potentials[i].c == current)
                                potentials.RemoveAt(i);

                        if((current % 13) > 0 && unvisited.Contains(current - 1))
                        {
                            if(vwalls.Contains(current))
                                potentials.Add(new Node() { t = 'v', p = current, c = current - 1 });
                            else if(!adjs.Contains(current - 1))
                                adjs.Add(current - 1);
                        }
                        if((current % 13) < 12 && unvisited.Contains(current + 1))
                        {
                            if(vwalls.Contains(current + 1))
                                potentials.Add(new Node() { t = 'v', p = current + 1, c = current + 1 });
                            else if(!adjs.Contains(current + 1))
                                adjs.Add(current + 1);
                        }
                        if(((current / 13) | 0) > 0 && unvisited.Contains(current - 13))
                        {
                            if(hwalls.Contains(current))
                                potentials.Add(new Node() { t = 'h', p = current, c = current - 13 });
                            else if(!adjs.Contains(current - 13))
                                adjs.Add(current - 13);
                        }
                        if(((current / 13) | 0) < 12 && unvisited.Contains(current + 13))
                        {
                            if(hwalls.Contains(current + 13))
                                potentials.Add(new Node() { t = 'h', p = current + 13, c = current + 13 });
                            else if(!adjs.Contains(current + 13))
                                adjs.Add(current + 13);
                        }
                    }
                }
            }

            return new Maze(new Vector2Int(13, 13), Enumerable.Range(0, 169).Select(i => hwalls.Contains(i)).ToArray(), Enumerable.Range(0, 169).Select(i => vwalls.Contains(i)).ToArray());
        }

        private struct Node
        {
            public char t;
            public int p, c;
        }
    }

#pragma warning disable 414
    private readonly string TwitchHelpMessage = @"Use ""!{0} show"" to focus the module. Use ""!{0} tap 12"" to tap the module when the timer matches those digits. Use ""!{0} colorblind"" to toggle colorblind mode.";
#pragma warning restore 414
    private IEnumerator ProcessTwitchCommand(string command)
    {
        command = command.ToLowerInvariant().Trim();
        if(command.Equals("colorblind") || command.Equals("cb"))
        {
            yield return null;
            SetCB(!_cbOn);
            yield return "sendtochat Colorblind mode is now " + (_cbOn ? "on" : "off") + ".";
            yield break;
        }

        if(command.Equals("tap"))
        {
            yield return null;
            GetComponent<KMSelectable>().Children[0].OnInteract();
            yield break;
        }

        Match m;
        if((m = Regex.Match(command, @"^tap\s+(\d\d?)$")).Success)
        {
            yield return null;
            int v = int.Parse(m.Groups[1].Value);
            while((int)(_info.GetTime() % 10) != v)
                GetComponent<KMSelectable>().Children[0].OnInteract();
            yield break;
        }
    }

    private class SearchNode
    {
        public int X, Y;
        public SearchNode Prev;
    }

    private IEnumerator TwitchHandleForcedSolve()
    {
        Log("Autosolving...");

        if(_mistakeMode > 0)
        {
            while(!_isSolved)
            {
                switch(_mistakeMode)
                {
                    case 1:
                        GetComponent<KMSelectable>().Children[0].OnInteract();
                        break;
                    case 2:
                        if(_info.GetSerialNumberNumbers().Last() == (int)(_info.GetTime() % 10))
                            GetComponent<KMSelectable>().Children[0].OnInteract();
                        break;
                    case 3:
                        if(_info.GetSerialNumberNumbers().Sum() == (int)(_info.GetTime() % 60))
                            GetComponent<KMSelectable>().Children[0].OnInteract();
                        break;
                }
                yield return true;
            }
            yield break;
        }

        if(!_activated)
        {
            if(_lastForce != 2)
            {
                _lastSelection = 2;
                Force();
                yield return new WaitForSeconds(2f);
            }
            _lastSelection = 0;
            Force();
            yield return new WaitForSeconds(2f);
        }

        Queue<SearchNode> searchSpace = new Queue<SearchNode>();
        List<Vector2Int> searched = new List<Vector2Int>();
        searchSpace.Enqueue(new SearchNode() { X = _x, Y = _y });
        SearchNode cur;

        while(true)
        {
            cur = searchSpace.Dequeue();
            if(cur.X == _tx && cur.Y == _ty)
                goto Done;
            searched.Add(new Vector2Int(cur.X, cur.Y));

            var avail = _maze.AvailableMoves(cur.X, cur.Y);
            if(avail[0] && !searched.Any(v => v.x == cur.X && v.y == cur.Y - 1) && !searchSpace.Any(v => v.X == cur.X && v.Y == cur.Y - 1))
                searchSpace.Enqueue(new SearchNode() { X = cur.X, Y = cur.Y - 1, Prev = cur });

            if(avail[1] && !searched.Any(v => v.x == cur.X && v.y == cur.Y - 1) && !searchSpace.Any(v => v.X == cur.X + 1 && v.Y == cur.Y))
                searchSpace.Enqueue(new SearchNode() { X = cur.X + 1, Y = cur.Y, Prev = cur });

            if(avail[2] && !searched.Any(v => v.x == cur.X && v.y == cur.Y - 1) && !searchSpace.Any(v => v.X == cur.X && v.Y == cur.Y + 1))
                searchSpace.Enqueue(new SearchNode() { X = cur.X, Y = cur.Y + 1, Prev = cur });

            if(avail[3] && !searched.Any(v => v.x == cur.X && v.y == cur.Y - 1) && !searchSpace.Any(v => v.X == cur.X - 1 && v.Y == cur.Y))
                searchSpace.Enqueue(new SearchNode() { X = cur.X - 1, Y = cur.Y, Prev = cur });
        }

        Done:
        List<SearchNode> path = new List<SearchNode>();
        while(cur.Prev != null)
        {
            path.Add(cur);
            cur = cur.Prev;
        }
        path.Add(cur);
        path.Reverse();
        for(int i = 0; i < path.Count - 1; i++)
        {
            int c = 2 * (path[i].X - path[i + 1].X) + path[i].Y - path[i + 1].Y;
            switch(c)
            {
                case 1:
                    _lastSelection = 0;
                    Force();
                    yield return new WaitForSeconds(2f);
                    break;
                case -1:
                    _lastSelection = 2;
                    Force();
                    yield return new WaitForSeconds(2f);
                    break;
                case 2:
                    _lastSelection = 3;
                    Force();
                    yield return new WaitForSeconds(2f);
                    break;
                case -2:
                    _lastSelection = 1;
                    Force();
                    yield return new WaitForSeconds(2f);
                    break;
            }
        }

        _lastSelection = 3;
        Force();
        yield return new WaitForSeconds(2f);

        _lastSelection = 1;
        Force();

        yield break;
    }
}
